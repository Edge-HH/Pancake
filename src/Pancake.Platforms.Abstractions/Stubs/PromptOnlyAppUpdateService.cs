using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>只提示、不自动覆盖的更新策略，用于 Linux 与 macOS。</summary>
public sealed class PromptOnlyAppUpdateService : IAppUpdateService
{
    public bool SupportsSelfUpdate => false;

    public bool TryLaunchInstaller(string path) => false;
}
