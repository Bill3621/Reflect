#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using FlaxEditor;
using FlaxEditor.GUI;
using FlaxEngine;
using FlaxEngine.Json;
using Debug = FlaxEngine.Debug;
using Process = System.Diagnostics.Process;

namespace Reflect;

public class ReflectEditorPlugin : EditorPlugin
{
    public override Type GamePluginType => typeof(ReflectPlugin);

    private ToolStripSeparator _separator;
    private ToolStripButton _serverButton;
    private Process _serverProcess;
    private readonly Queue<(LogType Type, string Message)> _serverLogs = new();
    private volatile Process _readyServer;
    private NetworkClient _autoStartedClient;

    private bool IsServerRunning => _serverProcess is { HasExited: false };

    public override void InitializeEditor()
    {
        base.InitializeEditor();

        if (Editor.IsHeadlessMode) return;

        _separator = Editor.UI.ToolStrip.AddSeparator();
        _serverButton = Editor.UI.ToolStrip.AddButton("Start Server");

        _serverButton.Clicked += OnServerButtonClicked;
        
        Editor.PlayModeBegin += OnPlayModeBegin;
        Editor.PlayModeEnd += OnPlayModeEnd;
        Scripting.Update += OnUpdate;

        UpdateButton();
    }

    public override void DeinitializeEditor()
    {
        if (!Editor.IsHeadlessMode)
        {
            Editor.PlayModeBegin -= OnPlayModeBegin;
            Editor.PlayModeEnd -= OnPlayModeEnd;
            Scripting.Update -= OnUpdate;
        }

        StopServer();

        if (_serverButton != null)
        {
            _serverButton.Clicked -= OnServerButtonClicked;
            _serverButton.Dispose();
            _serverButton = null;
        }

        _separator?.Dispose();
        _separator = null;

        base.DeinitializeEditor();
    }

    private void OnPlayModeBegin() => UpdateButton();
    
    private void OnPlayModeEnd()
    {
        StopServer();
        UpdateButton();
    }

    private void OnUpdate()
    {
        for (var i = 0; i < 100; i++)
        {
            (LogType Type, string Message) entry;

            lock (_serverLogs)
            {
                if (_serverLogs.Count == 0)
                    break;

                entry = _serverLogs.Dequeue();
            }

            var message = $"[Server] {entry.Message}";

            switch (entry.Type)
            {
                case LogType.Warning:
                    Debug.LogWarning(message);
                    break;

                case LogType.Error:
                case LogType.Fatal:
                    Debug.LogError(message);
                    break;

                case LogType.Info:
                default:
                    Debug.Log(message);
                    break;
            }
        }

        if (_serverProcess is { HasExited: true })
        {
            Debug.LogWarning($"Server exited with code {_serverProcess.ExitCode}.");
            StopServer();
            return;
        }

        if (!IsServerRunning || _readyServer != _serverProcess || NetworkManager.Instance == null)
            return;

        if (NetworkManager.IsClient)
        {
            _readyServer = null;
            return;
        }

        foreach (var ui in Level.GetScripts<NetworkManagerUI>())
        {
            _readyServer = null;
            try
            {
                ui.StartClient();
                _autoStartedClient = NetworkManager.Instance.Client;
                Debug.Log("[Reflect] Auto-started client.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reflect] Failed to auto-start client: {ex}");
            }
            break;
        }
    }

    private void OnServerButtonClicked()
    {
        if (!Editor.IsPlayMode)
            return;

        if (IsServerRunning)
            StopServer();
        else
            StartServer();

        UpdateButton();
    }
    
    private void UpdateButton()
    {
        if (_serverButton == null)
            return;

        _serverButton.Enabled = Editor.IsPlayMode;
        _serverButton.Text = IsServerRunning ? "Stop Server" : "Start Server";
    }

    private void StartServer()
    {
        if (!Editor.IsPlayMode || IsServerRunning) return;

        if (!Level.IsAnySceneLoaded || Level.Scenes.Length == 0)
        {
            Debug.LogError("No scene is loaded.");
            return;
        }

        var scene = Level.Scenes[0];
        var executable = Environment.ProcessPath;
        var project = Editor.GameProject.ProjectPath;

        if (string.IsNullOrEmpty(executable))
        {
            Debug.LogError("Cannot find Flax Editor executable.");
            return;
        }

        try
        {
            if (_serverProcess != null)
            {
                _serverProcess.Dispose();
                _serverProcess = null;
            }

            var settings = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            settings.ArgumentList.Add($"-project={project}");
            settings.ArgumentList.Add("-std");
            settings.ArgumentList.Add("-headless");
            settings.ArgumentList.Add("-null");
            settings.ArgumentList.Add("-mute");
            settings.ArgumentList.Add("-skipcompile");
            settings.ArgumentList.Add("-play");
            settings.ArgumentList.Add(JsonSerializer.GetStringID(scene.ID));

            settings.ArgumentList.Add("server");

            _serverProcess = Process.Start(settings);

            if (_serverProcess == null)
            {
                Debug.LogError("Failed to launch server.");
                return;
            }

            _serverProcess.OutputDataReceived += OnServerOutput;
            _serverProcess.BeginOutputReadLine();
            _serverProcess.ErrorDataReceived += OnServerErrorOutput;
            _serverProcess.BeginErrorReadLine();
            Debug.Log($"Started server process (PID {_serverProcess.Id})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start server: {ex}");
            StopServer();
        }
        
        UpdateButton();
    }

    private void StopServer()
    {
        Process process;
        lock (_serverLogs)
        {
            process = _serverProcess;
            _serverProcess = null;
            _readyServer = null;
            _serverLogs.Clear();
        }
        var client = _autoStartedClient;
        _autoStartedClient = null;

        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                Debug.Log("Server stop requested.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to stop server: {ex}");
        }
        finally
        {
            if (process != null)
            {
                process.OutputDataReceived -= OnServerOutput;
                process.ErrorDataReceived -= OnServerErrorOutput;
                process.Dispose();
            }
            client?.Disconnect();
            UpdateButton();
        }
    }
    
    private void OnServerErrorOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data?.Trim() != "REFLECT_SERVER_READY")
            return;

        lock (_serverLogs)
        {
            // Ignore delayed signals from a stopped process.
            if (sender != _serverProcess) return;
            _readyServer = (Process)sender;
            _serverLogs.Enqueue((LogType.Info, "Server ready signal received."));
        }
    }
    
    private void OnServerOutput(object sender, DataReceivedEventArgs e)
    {
        const string prefix = "REFLECT_LOG:";

        if (e.Data == null || !e.Data.StartsWith(prefix))
            return;

        try
        {
            var data = e.Data[prefix.Length..];
            var separator = data.IndexOf(':');
            if (separator < 0)
                return;

            var type = int.Parse(data[..separator]);
            var message = Encoding.UTF8.GetString(Convert.FromBase64String(data[(separator + 1)..]));
            
            lock (_serverLogs)
            {
                if (sender != _serverProcess) return;
                _serverLogs.Enqueue(((LogType)type, message));
            }
        }
        catch (FormatException)
        {
        }
    }
}
#endif