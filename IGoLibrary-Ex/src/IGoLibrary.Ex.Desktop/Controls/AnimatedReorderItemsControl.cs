using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace IGoLibrary.Ex.Desktop.Controls;

/// <summary>
/// Animates realized item containers from their previous layout position after a collection move.
/// </summary>
public sealed class AnimatedReorderItemsControl : ItemsControl
{
    private static readonly TimeSpan AnimationFrameInterval = TimeSpan.FromMilliseconds(16);
    private const double MinimumAnimatedOffset = 0.5;

    private readonly Dictionary<Control, RunningAnimation> _animations = [];
    private readonly DispatcherTimer _animationTimer;
    private readonly Dictionary<object, ItemLayout> _lastLayouts = new(ReferenceEqualityComparer.Instance);
    private INotifyCollectionChanged? _observableItems;
    private Dictionary<object, PreviousItemLayout>? _layoutsBeforeMove;
    private bool _isAttached;

    public AnimatedReorderItemsControl()
    {
        _animationTimer = new DispatcherTimer
        {
            Interval = AnimationFrameInterval
        };
        _animationTimer.Tick += OnAnimationTick;
        LayoutUpdated += OnLayoutUpdated;
    }

    public TimeSpan ReorderAnimationDuration { get; set; } = TimeSpan.FromMilliseconds(220);

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    internal static double CalculateTranslationOffset(double previousPosition, double currentPosition)
    {
        return previousPosition - currentPosition;
    }

    internal static double CalculateAnimatedOffset(double startingOffset, double progress)
    {
        var remaining = 1 - Math.Clamp(progress, 0, 1);
        return startingOffset * remaining * remaining * remaining;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        SubscribeToItems(ItemsSource as INotifyCollectionChanged);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        SubscribeToItems(null);
        StopAnimations();
        _layoutsBeforeMove = null;
        _lastLayouts.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_isAttached && change.Property == ItemsSourceProperty)
        {
            SubscribeToItems(change.NewValue as INotifyCollectionChanged);
        }
    }

    private void SubscribeToItems(INotifyCollectionChanged? items)
    {
        if (ReferenceEquals(_observableItems, items))
        {
            return;
        }

        if (_observableItems is not null)
        {
            _observableItems.CollectionChanged -= OnItemsCollectionChanged;
        }

        _observableItems = items;
        if (_observableItems is not null)
        {
            _observableItems.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Move)
        {
            return;
        }

        var layouts = _lastLayouts.Count > 0 ? _lastLayouts : CaptureLayouts();
        _layoutsBeforeMove = new Dictionary<object, PreviousItemLayout>(ReferenceEqualityComparer.Instance);
        foreach (var (item, layout) in layouts)
        {
            var animatedOffset = layout.Container.RenderTransform is TranslateTransform transform
                ? transform.Y
                : 0;
            _layoutsBeforeMove[item] = new PreviousItemLayout(layout.Position, animatedOffset);
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        var currentLayouts = CaptureLayouts();
        if (_layoutsBeforeMove is { Count: > 0 } previousLayouts)
        {
            AnimateMovedContainers(previousLayouts, currentLayouts);
            _layoutsBeforeMove = null;
        }

        _lastLayouts.Clear();
        foreach (var (item, layout) in currentLayouts)
        {
            _lastLayouts[item] = layout;
        }
    }

    private Dictionary<object, ItemLayout> CaptureLayouts()
    {
        var result = new Dictionary<object, ItemLayout>(ReferenceEqualityComparer.Instance);
        if (ItemsSource is not IEnumerable items)
        {
            return result;
        }

        var index = 0;
        foreach (var item in items)
        {
            if (item is not null && ContainerFromIndex(index) is { } container)
            {
                result[item] = new ItemLayout(container, container.Bounds.Y);
            }

            index++;
        }

        return result;
    }

    private void AnimateMovedContainers(
        IReadOnlyDictionary<object, PreviousItemLayout> previousLayouts,
        IReadOnlyDictionary<object, ItemLayout> currentLayouts)
    {
        if (ReorderAnimationDuration <= TimeSpan.Zero)
        {
            return;
        }

        foreach (var (item, currentLayout) in currentLayouts)
        {
            if (!previousLayouts.TryGetValue(item, out var previousLayout))
            {
                continue;
            }

            var offset = CalculateTranslationOffset(previousLayout.Position, currentLayout.Position) +
                         previousLayout.AnimatedOffset;
            if (Math.Abs(offset) < MinimumAnimatedOffset)
            {
                StopAnimation(currentLayout.Container);
                continue;
            }

            var transform = new TranslateTransform(0, offset);
            currentLayout.Container.RenderTransform = transform;
            _animations[currentLayout.Container] = new RunningAnimation(
                transform,
                offset,
                Stopwatch.GetTimestamp(),
                ReorderAnimationDuration);
        }

        if (_animations.Count > 0 && !_animationTimer.IsEnabled)
        {
            _animationTimer.Start();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        List<Control>? completed = null;

        foreach (var (container, animation) in _animations)
        {
            if (!ReferenceEquals(container.RenderTransform, animation.Transform))
            {
                (completed ??= []).Add(container);
                continue;
            }

            var elapsed = Stopwatch.GetElapsedTime(animation.StartTimestamp, now);
            var progress = elapsed.TotalMilliseconds / animation.Duration.TotalMilliseconds;
            animation.Transform.Y = CalculateAnimatedOffset(animation.StartingOffset, progress);

            if (progress >= 1)
            {
                animation.Transform.Y = 0;
                (completed ??= []).Add(container);
            }
        }

        if (completed is not null)
        {
            foreach (var container in completed)
            {
                _animations.Remove(container);
            }
        }

        if (_animations.Count == 0)
        {
            _animationTimer.Stop();
        }
    }

    private void StopAnimations()
    {
        _animationTimer.Stop();
        foreach (var animation in _animations.Values)
        {
            animation.Transform.Y = 0;
        }

        _animations.Clear();
    }

    private void StopAnimation(Control container)
    {
        if (_animations.Remove(container, out var animation))
        {
            animation.Transform.Y = 0;
        }

        if (container.RenderTransform is TranslateTransform transform)
        {
            transform.Y = 0;
        }

        if (_animations.Count == 0)
        {
            _animationTimer.Stop();
        }
    }

    private readonly record struct ItemLayout(Control Container, double Position);

    private readonly record struct PreviousItemLayout(double Position, double AnimatedOffset);

    private readonly record struct RunningAnimation(
        TranslateTransform Transform,
        double StartingOffset,
        long StartTimestamp,
        TimeSpan Duration);
}
