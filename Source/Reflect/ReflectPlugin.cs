using System;
using FlaxEngine;

namespace Reflect;

/// <summary>
/// Reflect — a lightweight, Mirror-inspired networking framework for Flax Engine.
/// </summary>
public class ReflectPlugin : GamePlugin
{
    public ReflectPlugin()
    {
        _description = new PluginDescription
        {
            Name = "Reflect",
            Category = "Networking",
            Author = "Bill (NotherStudios)",
            AuthorUrl = "https://billcodes.dev",
            HomepageUrl = "https://github.com/Bill3621/Reflect",
            RepositoryUrl = "https://github.com/Bill3621/Reflect",
            Description = "Lightweight client-server networking for Flax Engine. Transport abstraction, RPC system, SyncVars, and networked transform interpolation.",
            Version = new Version(0, 1, 0),
            IsAlpha = true,
            IsBeta = false,
        };
    }

    public override void Initialize()
    {
        base.Initialize();
        Debug.Log("[Reflect] Networking plugin initialized.");
    }

    public override void Deinitialize()
    {
        base.Deinitialize();
    }
}
