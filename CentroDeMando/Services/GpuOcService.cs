using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;

namespace CentroDeMando.Services;

public sealed record Range3(double Cur, double Min, double Max);
public sealed record Range4(double Cur, double Min, double Max, double Def);
public sealed record GpuOcDto(bool Available, string? Error, string? Name, Range3? CoreOffset, Range3? MemOffset, Range4? PowerLimit, Range4? TempLimit);

/// <summary>
/// Overclock de la GPU NVIDIA con las mismas llamadas del driver que usan las herramientas habituales:
/// desfase de reloj de núcleo y memoria, límite de potencia y límite térmico. No toca voltajes.
/// Los cambios se pierden al reiniciar.
/// </summary>
public sealed class GpuOcService
{
    readonly object _lock = new();
    bool _init;

    PhysicalGPU Gpu()
    {
        if (!_init) { NVIDIA.Initialize(); _init = true; }
        var gpus = PhysicalGPU.GetPhysicalGPUs();
        if (gpus.Length == 0) throw new InvalidOperationException("No se ha encontrado ninguna GPU NVIDIA.");
        return gpus[0];
    }

    public GpuOcDto Get()
    {
        lock (_lock)
        {
            try
            {
                var gpu = Gpu();
                Range3? core = null, mem = null; Range4? power = null, temp = null;
                string? partial = null;
                try
                {
                    var ps = GPUApi.GetPerformanceStates20(gpu.Handle);
                    var p0 = ps.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var c0) ? c0 : ps.Clocks.Values.First();
                    foreach (var c in p0)
                    {
                        var d = c.FrequencyDeltaInkHz;
                        var r = new Range3(d.DeltaValue / 1000.0, d.DeltaRange.Minimum / 1000.0, d.DeltaRange.Maximum / 1000.0);
                        if (c.DomainId == PublicClockDomain.Graphics) core = r;
                        else if (c.DomainId == PublicClockDomain.Memory) mem = r;
                    }
                }
                catch (Exception ex) { partial = "Relojes: " + ex.Message; }
                try
                {
                    var info = GPUApi.ClientPowerPoliciesGetInfo(gpu.Handle).PowerPolicyInfoEntries[0];
                    var st = GPUApi.ClientPowerPoliciesGetStatus(gpu.Handle).PowerPolicyStatusEntries[0];
                    power = new Range4(st.PowerTargetInPCM / 1000.0, info.MinimumPowerInPCM / 1000.0, info.MaximumPowerInPCM / 1000.0, info.DefaultPowerInPCM / 1000.0);
                }
                catch (Exception ex) { partial ??= "Potencia: " + ex.Message; }
                try
                {
                    var info = GPUApi.GetThermalPoliciesInfo(gpu.Handle).ThermalPoliciesInfoEntries[0];
                    var st = GPUApi.GetThermalPoliciesStatus(gpu.Handle).ThermalPoliciesStatusEntries[0];
                    temp = new Range4(st.TargetTemperature, info.MinimumTemperature, info.MaximumTemperature, info.DefaultTemperature);
                }
                catch (Exception ex) { partial ??= "Límite térmico: " + ex.Message; }

                bool any = core != null || mem != null || power != null || temp != null;
                return new GpuOcDto(any, any ? null : partial ?? "El driver no expone controles de overclock.", gpu.FullName, core, mem, power, temp);
            }
            catch (Exception ex)
            {
                return new GpuOcDto(false, ex.Message, null, null, null, null, null);
            }
        }
    }

    public GpuOcDto Apply(double? coreOffset, double? memOffset, double? powerLimit, double? tempLimit)
    {
        try { return ApplyCore(coreOffset, memOffset, powerLimit, tempLimit); }
        catch (Exception ex) when (ex.Message.Contains("INVALID_USER_PRIVILEGE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Para aplicar overclock a la GPU hay que abrir la app como administrador.");
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("NVIDIAApi"))
        {
            throw new InvalidOperationException("El driver de NVIDIA ha rechazado el cambio (" + ex.Message.Trim() + ").");
        }
    }

    GpuOcDto ApplyCore(double? coreOffset, double? memOffset, double? powerLimit, double? tempLimit)
    {
        lock (_lock)
        {
            var cur = Get();
            if (!cur.Available) throw new InvalidOperationException(cur.Error ?? "Overclock de GPU no disponible.");
            var gpu = Gpu();

            if ((coreOffset.HasValue && cur.CoreOffset != null) || (memOffset.HasValue && cur.MemOffset != null))
            {
                var clocks = new List<PerformanceStates20ClockEntryV1>();
                if (coreOffset.HasValue && cur.CoreOffset != null)
                {
                    int khz = (int)Math.Round(Math.Clamp(coreOffset.Value, cur.CoreOffset.Min, cur.CoreOffset.Max) * 1000);
                    clocks.Add(new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(khz)));
                }
                if (memOffset.HasValue && cur.MemOffset != null)
                {
                    int khz = (int)Math.Round(Math.Clamp(memOffset.Value, cur.MemOffset.Min, cur.MemOffset.Max) * 1000);
                    clocks.Add(new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(khz)));
                }
                var state = new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clocks.ToArray(),
                    Array.Empty<PerformanceStates20BaseVoltageEntryV1>());
                var info = new PerformanceStates20InfoV1(new[] { state }, (uint)clocks.Count, 0);
                GPUApi.SetPerformanceStates20(gpu.Handle, info);
            }
            if (powerLimit.HasValue && cur.PowerLimit != null)
            {
                uint pcm = (uint)Math.Round(Math.Clamp(powerLimit.Value, cur.PowerLimit.Min, cur.PowerLimit.Max) * 1000);
                GPUApi.ClientPowerPoliciesSetStatus(gpu.Handle, new PrivatePowerPoliciesStatusV1(
                    new[] { new PrivatePowerPoliciesStatusV1.PowerPolicyStatusEntry(pcm) }));
            }
            if (tempLimit.HasValue && cur.TempLimit != null)
            {
                int t = (int)Math.Round(Math.Clamp(tempLimit.Value, cur.TempLimit.Min, cur.TempLimit.Max));
                GPUApi.SetThermalPoliciesStatus(gpu.Handle, new PrivateThermalPoliciesStatusV2(
                    new[] { new PrivateThermalPoliciesStatusV2.ThermalPoliciesStatusEntry(ThermalController.GPU, t) }));
            }
            return Get();
        }
    }

    public GpuOcDto Reset()
    {
        var cur = Get();
        if (!cur.Available) return cur;
        return Apply(cur.CoreOffset != null ? 0 : null, cur.MemOffset != null ? 0 : null, cur.PowerLimit?.Def, cur.TempLimit?.Def);
    }
}
