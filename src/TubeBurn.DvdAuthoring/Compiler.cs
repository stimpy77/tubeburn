using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public sealed class DvdCompiler
{
    private readonly NativeAuthoringPipeline _nativeAuthoringPipeline = new();

    public NativeAuthoringPlan Compile(TubeBurnProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return _nativeAuthoringPipeline.CreatePlan(project);
    }

    public string Describe(NativeAuthoringPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return $"IFO titlesets={plan.Ifo.TitlesetCount}; PGCs={plan.Pgcs.Count}; VOB segments={plan.VobSegments.Count}; menus={plan.Menus.Count}";
    }
}
