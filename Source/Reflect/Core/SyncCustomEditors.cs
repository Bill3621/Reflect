#if FLAX_EDITOR
using System.Linq;
using System.Reflection;
using FlaxEditor.CustomEditors;
using FlaxEditor.CustomEditors.Editors;
using FlaxEditor.Scripting;
using FlaxEngine;

namespace Reflect;

[CustomEditor(typeof(SyncList<>))]
public class SyncListEditor : CustomEditor
{
    public override void Initialize(LayoutElementsContainer layout)
    {
        var prop = Values[0].GetType().GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

        if (prop == null) return;

        var listValues = new ValueContainer(new ScriptMemberInfo(prop));
        listValues.AddRange(Values.Select(prop.GetValue));

        var panel = layout.VerticalPanel();
        panel.Object(listValues, new ListEditor());

        panel.Panel.Enabled = false;
    }
}

[CustomEditor(typeof(SyncDictionary<,>))]
public class SyncDictionaryEditor : CustomEditor
{
    public override void Initialize(LayoutElementsContainer layout)
    {
        var prop = Values[0].GetType().GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

        if (prop == null) return;

        var dictValues = new ValueContainer(new ScriptMemberInfo(prop));
        dictValues.AddRange(Values.Select(prop.GetValue));

        var panel = layout.VerticalPanel();
        panel.Object(dictValues, new DictionaryEditor());
        
        panel.Panel.Enabled = false;
    }
}
#endif