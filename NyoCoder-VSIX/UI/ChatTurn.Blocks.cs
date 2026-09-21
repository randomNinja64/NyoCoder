using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    public partial class ChatTurn
    {
        internal enum CollapsibleBlockKind
        {
            Thinking,
            ToolCall,
            ToolOutput
        }

        /// <summary>
        /// Shared UI state for thinking / tool-call / tool-output expanders.
        /// </summary>
        internal sealed class CollapsibleBlockState
        {
            public ContentRecord Record;
            public bool Active;
            public string Name;
            public CollapsibleBlockKind Kind;
            public Expander Expander;
            public TextBlock HeaderLabel;
            public TextBlock BodyText;
            public DispatcherTimer Timer;
            public int EllipsisCount = 1;
            public DateTime StartedUtc;
            public int DurationSeconds;

            public bool Collapsed
            {
                get { return !Expander.IsExpanded; }
            }
        }

        private void StartCollapsibleBlock(string name, CollapsibleBlockKind kind)
        {
            // Drop an empty trailing paragraph left by content setup so the
            // expander is the next visible block (no blank line above it).
            Paragraph last = Document.Blocks.LastBlock as Paragraph;
            if (last != null
                && string.IsNullOrWhiteSpace(new TextRange(last.ContentStart, last.ContentEnd).Text))
            {
                Document.Blocks.Remove(last);
            }

            ChatBlockDisplayMode mode = GetDisplayMode(kind);
            // Shown and Hidden (name-only tool call) start expanded; Collapsed does not.
            bool expandByDefault = _restoring ? _restoringRecord.Expanded : mode != ChatBlockDisplayMode.Collapsed;
            ContentRecord record = _restoring ? _restoringRecord : new ContentRecord
            {
                Kind = kind, Name = name, Expanded = expandByDefault
            };
            if (!_restoring) _content.Add(record);

            var state = new CollapsibleBlockState
            {
                Record = record,
                Active = !_restoring,
                Name = name,
                Kind = kind,
                EllipsisCount = 1,
                StartedUtc = DateTime.UtcNow
            };

            state.HeaderLabel = new TextBlock();
            state.HeaderLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);

            state.BodyText = new TextBlock
            {
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                TextWrapping = TextWrapping.Wrap
            };
            state.BodyText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);

            state.Expander = new Expander
            {
                Header = state.HeaderLabel,
                Content = state.BodyText,
                IsExpanded = expandByDefault,
                Style = ThinkingExpanderStyle,
                Tag = state
            };
            state.Expander.SetResourceReference(Control.ForegroundProperty, VsBrushes.WindowTextKey);
            state.Expander.Expanded += OnBlockExpanded;
            state.Expander.Collapsed += OnBlockCollapsed;

            Document.Blocks.Add(new BlockUIContainer(state.Expander)
            {
                Margin = new Thickness(0)
            });

            _activeBlock = state;
            state.HeaderLabel.Text = BuildLabelText(state);

            if (_viewActive && state.Active && state.Collapsed)
                StartEllipsisTimer(state);
        }

        private static ChatBlockDisplayMode GetDisplayMode(CollapsibleBlockKind kind)
        {
            switch (kind)
            {
                case CollapsibleBlockKind.ToolCall:
                    return ConfigHandler.GetToolCallDisplayMode();
                case CollapsibleBlockKind.ToolOutput:
                    return ConfigHandler.GetToolOutputDisplayMode();
                default:
                    return ConfigHandler.GetThinkingDisplayMode();
            }
        }

        private void EndCollapsibleBlock()
        {
            CollapsibleBlockState state = _activeBlock;
            if (state == null)
                return;

            state.Active = false;
            state.DurationSeconds = _restoring ? state.Record.DurationSeconds
                : Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.StartedUtc).TotalSeconds));
            state.Record.DurationSeconds = state.DurationSeconds;
            _activeBlock = null;
            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);

            if (state.BodyText.Text != null)
                state.BodyText.Text = state.BodyText.Text.TrimEnd('\r', '\n');
        }

        private void OnBlockExpanded(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            CollapsibleBlockState state = expander != null ? expander.Tag as CollapsibleBlockState : null;
            if (state == null)
                return;

            state.Record.Expanded = true;
            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);
        }

        private void OnBlockCollapsed(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            CollapsibleBlockState state = expander != null ? expander.Tag as CollapsibleBlockState : null;
            if (state == null)
                return;

            state.Record.Expanded = false;
            if (_viewActive && state.Active)
                StartEllipsisTimer(state);
            else
                state.HeaderLabel.Text = BuildLabelText(state);
        }

        private static void StartEllipsisTimer(CollapsibleBlockState state)
        {
            if (state.Timer == null)
            {
                state.Timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(450)
                };
                state.Timer.Tick += (s, e) => OnEllipsisTick(state);
            }

            state.HeaderLabel.Text = BuildLabelText(state);
            if (!state.Timer.IsEnabled)
                state.Timer.Start();
        }

        private static void StopEllipsisTimer(CollapsibleBlockState state)
        {
            if (state.Timer == null)
                return;

            state.Timer.Stop();
        }

        private static void OnEllipsisTick(CollapsibleBlockState state)
        {
            if (!state.Active || !state.Collapsed)
            {
                StopEllipsisTimer(state);
                state.HeaderLabel.Text = BuildLabelText(state);
                return;
            }

            state.EllipsisCount = state.EllipsisCount >= 3 ? 1 : state.EllipsisCount + 1;
            state.HeaderLabel.Text = BuildLabelText(state);
        }

        private static string BuildLabelText(CollapsibleBlockState state)
        {
            if (state.Kind == CollapsibleBlockKind.ToolCall)
            {
                string baseLabel = "tool call: " + (state.Name ?? "tool");
                if (state.Collapsed && state.Active)
                    return baseLabel + new string('.', state.EllipsisCount);
                return baseLabel;
            }

            if (state.Kind == CollapsibleBlockKind.ToolOutput)
            {
                if (state.Collapsed && state.Active)
                    return "tool output" + new string('.', state.EllipsisCount);
                return "tool output";
            }

            if (state.Collapsed && state.Active)
                return "thinking" + new string('.', state.EllipsisCount);

            if (state.Collapsed && !state.Active)
            {
                int seconds = state.DurationSeconds;
                return "thought for " + seconds + " second" + (seconds == 1 ? "" : "s");
            }

            return "thinking";
        }

    }
}
