namespace HoloRepository.Core.Markup;

public enum AnnotationChange
{
    Added,
    Removed,
    Cleared,
}

public sealed class AnnotationChangedEventArgs(AnnotationChange change, Annotation? annotation) : EventArgs
{
    public AnnotationChange Change { get; } = change;
    public Annotation? Annotation { get; } = annotation;
}

/// <summary>Thread-safe store shared by every viewer window and the collaboration hub.</summary>
public sealed class AnnotationStore
{
    private readonly List<Annotation> _items = new();
    private readonly object _gate = new();

    public event EventHandler<AnnotationChangedEventArgs>? Changed;

    public IReadOnlyList<Annotation> All
    {
        get
        {
            lock (_gate) return _items.ToList();
        }
    }

    public IReadOnlyList<Annotation> ForSeries(string seriesId)
    {
        lock (_gate) return _items.Where(a => a.SeriesId == seriesId).ToList();
    }

    /// <summary>Adds an annotation. Returns false if one with the same id already exists (e.g. an echoed sync message).</summary>
    public bool Add(Annotation annotation)
    {
        lock (_gate)
        {
            if (_items.Any(a => a.Id == annotation.Id)) return false;
            _items.Add(annotation);
        }
        Changed?.Invoke(this, new AnnotationChangedEventArgs(AnnotationChange.Added, annotation));
        return true;
    }

    public bool Remove(Guid id)
    {
        Annotation? removed;
        lock (_gate)
        {
            removed = _items.FirstOrDefault(a => a.Id == id);
            if (removed is null) return false;
            _items.Remove(removed);
        }
        Changed?.Invoke(this, new AnnotationChangedEventArgs(AnnotationChange.Removed, removed));
        return true;
    }

    public void Clear()
    {
        lock (_gate) _items.Clear();
        Changed?.Invoke(this, new AnnotationChangedEventArgs(AnnotationChange.Cleared, null));
    }

    public int NextNumber(AnnotationKind kind)
    {
        lock (_gate) return _items.Count(a => a.Kind == kind) + 1;
    }
}
