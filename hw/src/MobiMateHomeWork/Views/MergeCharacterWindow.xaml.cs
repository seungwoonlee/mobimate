using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork;

/// <summary>
/// 캐릭터 합치기: 이 줄의 기록을 합칠 대상 캐릭터를 고른다 (같은 직업 먼저, "모든 직업 보기"로 같은 서버 전체).
/// 이 창은 고르기만 한다 — 확인 대화상자와 실제 합치기는 부른 쪽이 한다.
/// </summary>
public partial class MergeCharacterWindow : Window
{
    private sealed record Item(MergeCandidate Candidate)
    {
        public string Label => $"{Candidate.Name}   ·   {Candidate.Detail}";
    }

    private readonly MergeRequest _request;

    public MergeCharacterWindow(MergeRequest request)
    {
        InitializeComponent();
        _request = request;
        Intro.Text = $"'{request.FromName}'의 기록을 어느 캐릭터로 합칠까요?";
        var hasSameJob = request.Candidates.Any(c => c.SameJob);
        AllJobs.IsChecked = !hasSameJob;       // 같은 직업 후보가 없으면 처음부터 같은 서버 전체
        AllJobs.Visibility = hasSameJob && request.Candidates.Any(c => !c.SameJob) ? Visibility.Visible : Visibility.Collapsed;
        List.SelectionChanged += (_, _) => Next.IsEnabled = List.SelectedItem != null;
        Fill();
    }

    /// <summary>고른 대상의 키. 취소하면 null.</summary>
    public string? SelectedKey { get; private set; }

    public string? SelectedName => (List.SelectedItem as Item)?.Candidate.Name;

    private void Fill()
    {
        var all = AllJobs.IsChecked == true;
        var items = _request.Candidates.Where(c => all || c.SameJob).Select(c => new Item(c)).ToList();
        List.ItemsSource = items;
        if (items.Count > 0) List.SelectedIndex = 0;
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => Fill();

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OnNext(sender, e);

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not Item item) return;
        SelectedKey = item.Candidate.Key;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
