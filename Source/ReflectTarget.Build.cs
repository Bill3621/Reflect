using Flax.Build;

public class ReflectTarget : GameProjectTarget
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();

        Modules.Add("Reflect");
    }
}
