using Flax.Build;

public class ReflectEditorTarget : GameProjectEditorTarget
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();

        Modules.Add("Reflect");
    }
}
