using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork.Views;

/// <summary>
/// 숙제 표. DataContext = <see cref="HomeworkBoardViewModel"/>. 줄 안 어디를 눌러도(칸 포함) 그 캐릭터가 선택돼 오른쪽 카드가 바뀐다.
/// 칸을 누르는 동작 자체는 칸 단추의 명령이 한다.
/// </summary>
public partial class HomeworkTableView : UserControl
{
    public HomeworkTableView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is HomeworkBoardViewModel oldBoard) oldBoard.SeasonEditRequested -= EditSeason;
            if (e.NewValue is HomeworkBoardViewModel newBoard) newBoard.SeasonEditRequested += EditSeason;
        };
    }

    private async void EditSeason(CellViewModel cell)
    {
        if (DataContext is not HomeworkBoardViewModel board) return;
        var expected = cell.Value ?? 0;
        var dialog = new SeasonValueWindow(cell) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) await board.SetSeasonValueAsync(cell, dialog.Value, expected);
    }

    /// <summary>단계 카운터 칸을 우클릭하면 한 단계 내린다.</summary>
    private async void OnCellRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CellViewModel { IsStep: true } cell } && DataContext is HomeworkBoardViewModel board)
        {
            e.Handled = true;
            await board.StepBackAsync(cell);
        }
    }

    private void OnRowPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CharacterRowViewModel row } && DataContext is HomeworkBoardViewModel board)
            board.SelectRow(row);
    }

    /// <summary>합치기를 할 수 없는 화면(읽기 전용)이면 메뉴를 열지 않는다. 우클릭한 줄도 선택해 카드가 따라오게 한다.</summary>
    private void OnRowMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not HomeworkBoardViewModel { CanMerge: true } board) { e.Handled = true; return; }
        if (sender is FrameworkElement { DataContext: CharacterRowViewModel row }) board.SelectRow(row);
    }

    /// <summary>줄 우클릭 "다른 캐릭터와 합치기…": 이 줄의 기록을 어느 캐릭터로 합칠지 고르는 창을 요청한다.</summary>
    private void OnMergeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CharacterRowViewModel row } && DataContext is HomeworkBoardViewModel board)
            board.BeginMerge(row);
    }

    /// <summary>표를 맨 위로 되돌린다 (다른 계정으로 바꿨을 때).</summary>
    public void ScrollToTop() => RowScroll.ScrollToVerticalOffset(0);
}
