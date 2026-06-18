using Flax.Build;

public class ReflectTarget : GameProjectTarget
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();
        
        // Reference the modules for game
        Modules.Add("Reflect");
    }
}
