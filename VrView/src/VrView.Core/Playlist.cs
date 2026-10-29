namespace VrView.Core;

public enum RepeatMode
{
    Off,
    All,
    One,
}

/// <summary>A playable entry: an opaque location (content URI) and a display name.</summary>
public sealed record MediaItem(string Location, string Name, long SizeBytes = 0);

/// <summary>
/// Ordered list of videos with shuffle and repeat. Shuffle keeps a play order permutation so
/// "previous" walks back through what was actually played.
/// </summary>
public sealed class Playlist
{
    private readonly List<MediaItem> _items;
    private int[] _order;
    private int _position;
    private bool _shuffle;
    private readonly Random _random;

    public Playlist(IEnumerable<MediaItem> items, int startIndex = 0, bool shuffle = false,
        RepeatMode repeat = RepeatMode.All, Random? random = null)
    {
        _items = items.ToList();
        _random = random ?? new Random();
        _order = Enumerable.Range(0, _items.Count).ToArray();
        _position = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
        Repeat = repeat;
        if (shuffle) Shuffle = true;
    }

    public IReadOnlyList<MediaItem> Items => _items;
    public int Count => _items.Count;
    public RepeatMode Repeat { get; set; }

    /// <summary>Index into <see cref="Items"/> of the current entry, or -1 when empty.</summary>
    public int CurrentIndex => _position < 0 ? -1 : _order[_position];

    public MediaItem? Current => _position < 0 ? null : _items[_order[_position]];

    /// <summary>Current play order (indices into <see cref="Items"/>).</summary>
    public IReadOnlyList<int> Order => _order;

    /// <summary>
    /// Enabling shuffle randomizes the remaining order with the current item first; disabling it
    /// restores the natural order while staying on the current item.
    /// </summary>
    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value && _order.Length == _items.Count) return;
            _shuffle = value;
            int current = CurrentIndex;
            if (value)
            {
                _order = MakeShuffledOrder(first: current);
                _position = current < 0 ? -1 : 0;
            }
            else
            {
                _order = Enumerable.Range(0, _items.Count).ToArray();
                _position = current;
            }
        }
    }

    private int[] MakeShuffledOrder(int first, int avoidFirst = -1)
    {
        var order = Enumerable.Range(0, _items.Count).ToArray();
        // Fisher-Yates
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        if (first >= 0)
        {
            int at = Array.IndexOf(order, first);
            (order[0], order[at]) = (order[at], order[0]);
        }
        else if (avoidFirst >= 0 && order.Length > 1 && order[0] == avoidFirst)
        {
            // Avoid playing the same video twice in a row when a shuffled cycle restarts.
            int swapWith = 1 + _random.Next(order.Length - 1);
            (order[0], order[swapWith]) = (order[swapWith], order[0]);
        }
        return order;
    }

    /// <summary>Jumps to a specific entry of <see cref="Items"/>.</summary>
    public void JumpTo(int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= _items.Count) throw new ArgumentOutOfRangeException(nameof(itemIndex));
        _position = Array.IndexOf(_order, itemIndex);
    }

    /// <summary>
    /// Called when the current video ends. Returns false when playback should stop
    /// (repeat off and the end of the list was reached).
    /// </summary>
    public bool AdvanceOnCompletion()
    {
        if (_position < 0) return false;
        if (Repeat == RepeatMode.One) return true;
        if (_position + 1 < _order.Length) { _position++; return true; }
        if (Repeat == RepeatMode.Off) return false;
        WrapToStart();
        return true;
    }

    /// <summary>User pressed "next": always moves, wrapping around at the end.</summary>
    public bool Next()
    {
        if (_position < 0) return false;
        if (_position + 1 < _order.Length) _position++;
        else WrapToStart();
        return true;
    }

    /// <summary>User pressed "previous": moves back in play order, wrapping at the start.</summary>
    public bool Previous()
    {
        if (_position < 0) return false;
        _position = _position > 0 ? _position - 1 : _order.Length - 1;
        return true;
    }

    private void WrapToStart()
    {
        if (_shuffle && _order.Length > 1)
        {
            int last = CurrentIndex;
            _order = MakeShuffledOrder(first: -1, avoidFirst: last);
        }
        _position = 0;
    }

    public static RepeatMode NextRepeatMode(RepeatMode mode) => mode switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.Off,
    };

    public static string RepeatLabel(RepeatMode mode) => mode switch
    {
        RepeatMode.Off => "Repetir: no",
        RepeatMode.All => "Repetir: todo",
        _ => "Repetir: uno",
    };
}
