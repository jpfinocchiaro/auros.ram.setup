namespace Auros.Ram.Core.Services;

/// <summary>Historial de deshacer/rehacer de la sesión (RNF-28).</summary>
public sealed class UndoStack
{
    private sealed record Entry(long Id, string Description, Action Undo, Action Redo);

    private readonly LinkedList<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();
    private long _nextId = 1;

    public UndoStack(int capacity = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Last?.Value.Description;
    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    /// <summary>Identifica el estado actual; sirve para saber si hay cambios sin guardar.</summary>
    public long StateId => _undo.Last?.Value.Id ?? 0;

    public event EventHandler? Changed;

    /// <summary>Registra una acción ya aplicada.</summary>
    public void Push(string description, Action undo, Action redo)
    {
        _undo.AddLast(new Entry(_nextId++, description, undo, redo));
        while (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Last is not { } node)
        {
            return;
        }

        _undo.RemoveLast();
        node.Value.Undo();
        _redo.Push(node.Value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var entry = _redo.Pop();
        entry.Redo();
        _undo.AddLast(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
