using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdvApp.Models;
using Wpf.Ui.Controls;

namespace MdvApp;

/// <summary>A read-only viewer for a file's bytes, with searchable Hex and Text tabs.</summary>
public partial class FileViewerWindow : FluentWindow
{
    private readonly List<int> _matches = new();
    private int _matchIndex = -1;
    private string _query = string.Empty;

    public FileViewerWindow(string fileName, byte[] data, bool preferHex)
    {
        InitializeComponent();

        Title = fileName;
        ViewerTitleBar.Title = $"{fileName}  ·  {data.Length:N0} bytes";

        HexView.Text = string.Join("\r\n", HexDump.ToRows(data));
        TextView.Text = HexDump.ToText(data);

        ViewTabs.SelectedIndex = preferHex ? 0 : 1;
    }

    private System.Windows.Controls.TextBox ActiveView => ViewTabs.SelectedIndex == 0 ? HexView : TextView;

    private void OnSearch(object sender, RoutedEventArgs e) => RunSearch();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Enter searches (or steps to the next hit); Shift+Enter steps back.
        if (_query == SearchBox.Text && _matches.Count > 0)
            Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        else
            RunSearch();

        e.Handled = true;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        // Clearing the search box returns the viewer to its initial state.
        if (string.IsNullOrEmpty(SearchBox.Text))
            ClearSearchState();
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only react to the TabControl's own selection, not nested events.
        if (!ReferenceEquals(e.Source, ViewTabs))
            return;

        // Switching tabs resets to the initial state.
        SearchBox.Clear();
        ClearSearchState();
        ActiveView.ScrollToHome();
    }

    private void ClearSearchState()
    {
        _matches.Clear();
        _matchIndex = -1;
        _query = string.Empty;

        SearchResults.Visibility = Visibility.Collapsed;
        MatchCounter.Text = string.Empty;
        PrevButton.IsEnabled = false;
        NextButton.IsEnabled = false;

        HexView.Select(0, 0);
        TextView.Select(0, 0);
    }

    private void RunSearch()
    {
        _query = SearchBox.Text ?? string.Empty;
        _matches.Clear();
        _matchIndex = -1;

        if (_query.Length > 0)
        {
            string haystack = ActiveView.Text;
            int from = 0;
            while (true)
            {
                int hit = haystack.IndexOf(_query, from, StringComparison.OrdinalIgnoreCase);
                if (hit < 0)
                    break;
                _matches.Add(hit);
                from = hit + 1;
            }
        }

        SearchResults.Visibility = _query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        bool hasMatches = _matches.Count > 0;
        PrevButton.IsEnabled = hasMatches;
        NextButton.IsEnabled = hasMatches;

        if (hasMatches)
            GoToMatch(0);
        else
            MatchCounter.Text = _query.Length == 0 ? string.Empty : "No matches";
    }

    private void OnNextMatch(object sender, RoutedEventArgs e) => Step(1);

    private void OnPrevMatch(object sender, RoutedEventArgs e) => Step(-1);

    private void Step(int direction)
    {
        if (_matches.Count == 0)
            return;
        int next = (_matchIndex + direction + _matches.Count) % _matches.Count;
        GoToMatch(next);
    }

    private void GoToMatch(int index)
    {
        _matchIndex = index;
        int start = _matches[index];

        var view = ActiveView;
        view.Focus();
        view.Select(start, _query.Length);
        view.ScrollToLine(view.GetLineIndexFromCharacterIndex(start));

        MatchCounter.Text = $"{index + 1} / {_matches.Count}";
    }
}
