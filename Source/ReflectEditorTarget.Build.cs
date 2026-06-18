using Flax.Build;

public class ReflectEditorTarget : ProjectTargetBase
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();
        
        // Reference the modules for editor
        Modules.Add("Reflect");
    }
}
