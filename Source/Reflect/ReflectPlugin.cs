using System;
using System.Text;
using FlaxEngine;

namespace Reflect;

/// <summary>
/// Reflect — a lightweight, Mirror-inspired networking framework for Flax Engine.
/// </summary>
public class ReflectPlugin : GamePlugin
{
    private bool _forwardLogs;
    
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

        if (!Engine.IsHeadless || !Engine.CommandLine.Contains("server")) return;
        
        Debug.Logger.LogHandler.SendLog += ForwardLog;
        Debug.Logger.LogHandler.SendExceptionLog += ForwardException;
        _forwardLogs = true;
    }

    public override void Deinitialize()
    {
        base.Deinitialize();

        if (!_forwardLogs) return;
        Debug.Logger.LogHandler.SendLog -= ForwardLog;
        Debug.Logger.LogHandler.SendExceptionLog -= ForwardException;
        _forwardLogs = false;
    }

    private static void ForwardLog(LogType type, string message, FlaxEngine.Object context, string stackTrace) => SendLog(type, message);
    private static void ForwardException(Exception exception, FlaxEngine.Object context) => SendLog(LogType.Error, exception.ToString());

    private static void SendLog(LogType type, string message)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(message));
        Console.WriteLine($"REFLECT_LOG:{(int)type}:{encoded}");
        Console.Out.Flush();
    } 
}
