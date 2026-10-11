using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork.Views;

/// <summary>
/// 캐릭터 카드. 이름을 누르면 그 자리에서 입력 칸으로 바뀐다 — Enter 저장, Esc 취소(칸을 떠나도 취소).
/// </summary>
public partial class CharacterCardView : UserControl
{
    public CharacterCardView() => InitializeComponent();

    private HomeworkBoardViewModel? Board => DataContext as HomeworkBoardViewModel;

    private void OnNameClicked(object sender, MouseButtonEventArgs e)
    {
        Board?.BeginRename();
        e.Handled = true;
    }

    private void OnRenameButton(object sender, RoutedEventArgs e) => Board?.BeginRename();

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (Board is { } b) _ = b.CommitRenameAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Board?.CancelRename();
        }
    }

    /// <summary>
    /// 입력 칸에서 포커스가 창 안 다른 곳으로 옮겨 가면(다른 곳을 누름) 저장하지 않고 닫는다.
    /// 창 자체가 비활성이 되어(다른 프로그램을 누름) 포커스가 창 밖으로 나가는 것은 취소로 보지 않는다 — 돌아오면 쓰던 입력이 그대로다.
    /// </summary>
    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus == null) return;
        if (Board is { Card.IsRenaming: true } b && !NameBox.IsKeyboardFocusWithin) b.CancelRename();
    }

    private void OnNameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        // 보이게 된 직후 레이아웃이 끝난 뒤에 포커스를 주고 글자를 모두 선택한다
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            Keyboard.Focus(NameBox);
            NameBox.SelectAll();
        }, DispatcherPriority.Input);
    }
}
