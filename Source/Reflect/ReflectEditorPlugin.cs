#if FLAX_EDITOR
using System;
using System.Diagnostics;
using System.IO;
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

        _serverButton?.Dispose();
        _serverButton = null;

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
        if (_serverProcess is not { HasExited: true }) return;
        
        Debug.LogWarning($"Server exited with code {_serverProcess.ExitCode}.");
        
        _serverProcess.Dispose();
        _serverProcess = null;
        
        UpdateButton();
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
                CreateNoWindow = true
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

            Debug.Log($"Started server process (PID {_serverProcess.Id})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start server: {ex}");
            _serverProcess?.Dispose();
            _serverProcess = null;
        }
        
        UpdateButton();
    }

    private void StopServer()
    {
        if (_serverProcess == null)
            return;

        try
        {
            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                Debug.Log("Server stop requested.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to stop server: {ex}");
        }
        finally
        {
            _serverProcess.Dispose();
            _serverProcess = null;
        }

        UpdateButton();
    }
}
#endif