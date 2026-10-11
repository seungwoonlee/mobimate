using System.Windows.Controls;

namespace MobiMateHomeWork.Views;

/// <summary>칸 상태 기호(완료 ✓ / 미완료 ○ / 미확인 ·). 크기는 FontSize로 정한다.</summary>
public partial class StateGlyph : UserControl
{
    public StateGlyph() => InitializeComponent();
}
