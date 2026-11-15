using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Collections;
using Avalonia.Controls;

namespace NksHub.Textida.App.Shell;

internal enum StructureTreeNodeKind { Normal, Loading, Pager }

internal sealed class StructureTreeNode : INotifyPropertyChanged
{
    private readonly HashSet<StructureTreeNode> attachedChildren = [];
    private string heading = "", value = "";
    private object? tag;
    private bool expanded, hasChildren, caret, enabled = true;
    private StructureTreeNodeKind kind;
    private Control? pagerContent;
    private long bindingVersion;
    private bool bindingValid = true;

    public StructureTreeNode() => Items.CollectionChanged += ChildrenChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
    public AvaloniaList<StructureTreeNode> Items { get; } = [];
    public StructureTreeNode? Parent { get; private set; }
    public long BindingVersion => bindingVersion;
    public bool IsBindingValid => bindingValid;
    public string Heading { get => heading; set => Set(ref heading, value); }
    public string Value { get => value; set => Set(ref this.value, value); }
    public string DisplayName => (Heading + " " + Value).TrimEnd();
    public object? Tag
    {
        get => tag;
        set
        {
            if (Equals(tag, value)) return;
            tag = value;
            Invalidate(); Changed();
        }
    }
    public bool IsExpanded { get => expanded; set => Set(ref expanded, value); }
    public bool HasChildren { get => hasChildren; set => Set(ref hasChildren, value); }
    public bool IsCaret { get => caret; set => Set(ref caret, value); }
    public bool IsEnabled { get => enabled; set => Set(ref enabled, value); }
    public StructureTreeNodeKind Kind { get => kind; set => Set(ref kind, value); }
    public Control? PagerContent { get => pagerContent; set => Set(ref pagerContent, value); }

    public void Update(string title, string preview, object? identity, bool expandable)
    {
        heading = title; value = preview; tag = identity; hasChildren = expandable;
        bindingValid = true; bindingVersion++;
        Changed(nameof(BindingVersion)); Changed(nameof(IsBindingValid));
        Changed(nameof(Heading)); Changed(nameof(Value)); Changed(nameof(Tag)); Changed(nameof(HasChildren));
    }

    public void Invalidate()
    {
        bindingVersion++;
        Changed(nameof(BindingVersion));
    }

    public void Reset()
    {
        // Retired identities stay invalid until Update publishes the replacement.
        bindingValid = false; bindingVersion++;
        heading = value = ""; tag = null;
        expanded = hasChildren = caret = false; enabled = true;
        kind = StructureTreeNodeKind.Normal; pagerContent = null;
        Parent?.Items.Remove(this);
        Items.Clear();
        Changed(nameof(BindingVersion)); Changed(nameof(IsBindingValid));
        Changed(nameof(Heading)); Changed(nameof(Value)); Changed(nameof(Tag));
        Changed(nameof(IsExpanded)); Changed(nameof(HasChildren)); Changed(nameof(IsCaret));
        Changed(nameof(IsEnabled)); Changed(nameof(Kind)); Changed(nameof(PagerContent));
    }

    private void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var current = Items.ToHashSet();
        foreach (var old in attachedChildren)
            if (!current.Contains(old) && ReferenceEquals(old.Parent, this)) old.SetParent(null);
        foreach (var child in current)
        {
            for (StructureTreeNode? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
                if (ReferenceEquals(child, ancestor)) throw new InvalidOperationException("A tree node cannot contain its ancestor.");
            if (ReferenceEquals(child.Parent, this)) continue;
            child.Parent?.Items.Remove(child);
            child.SetParent(this);
        }
        attachedChildren.Clear(); attachedChildren.UnionWith(current);
        Changed(nameof(Items));
    }

    private void SetParent(StructureTreeNode? parent)
    {
        Parent = parent;
        Changed(nameof(Parent));
    }

    private void Set<T>(ref T field, T next, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, next)) return;
        field = next; Changed(property);
    }

    private void Changed([CallerMemberName] string? property = null)
        => PropertyChanged?.Invoke(this, new(property));
}
