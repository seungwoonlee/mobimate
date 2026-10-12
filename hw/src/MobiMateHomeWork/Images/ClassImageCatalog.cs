namespace MobiMateHomeWork.Images;

/// <summary>
/// 직업별 전신 일러스트 목록(웹앱 ClassImages.Known과 같은 27개, 같은 넥슨 이미지 서버 주소)과 직업 이름 → 이미지 id 표(웹앱 lib/job.ts와 같음).
/// 이미지는 배포 파일에 넣지 않고 첫 실행 때 이 PC에만 내려받는다(저작권, 웹앱 직업 이미지와 같은 방식). 직업은 얼굴이 아니라 전체 실루엣으로 구분하므로 전신을 자르지 않는다.
/// </summary>
public static class ClassImageCatalog
{
    public const string FolderName = "class-images";
    private const string Base = "https://lwi.nexon.com/m_mabinogim/brand/info/class/";

    private static RemoteImage I(string id, string file) => new(id, Base + file + ".png");

    public static readonly IReadOnlyList<RemoteImage> Known = new[]
    {
        I("warrior_1", "warrior_line_1_06066EE99E761EFA"), I("warrior_2", "warrior_line_5_8A452CCB080CB6B1"), I("warrior_3", "warrior_line_2_37B2D3A04C09CC2F"),
        I("warrior_4", "warrior_line_3_68958D98B3DDC1BF"), I("warrior_5", "warrior_line_4_5F017124236C03D6"),
        I("archer_1", "archer_line_1_91D9F178871DF33E"), I("archer_2", "archer_line_2_946AEAF0A67A9596"), I("archer_3", "archer_line_3_1C943631C2AC78C3"),
        I("archer_4", "archer_line_4_87DDBBCFAFAB46A2"),
        I("thief_1", "thief_line_1_329BACBB215B9685"), I("thief_2", "thief_line_2_329BACBB215B9685"), I("thief_3", "thief_line_3_329BACBB215B9685"),
        I("thief_4", "thief_line_4_329BACBB215B9685"),
        I("mage_1", "mage_line_1_ABCF78594DE826C6"), I("mage_2", "mage_line_5_B081998986AB2402"), I("mage_3", "mage_line_2_3B83737BEFFE2F1E"),
        I("mage_4", "mage_line_3_8B52C51AC86C7FF4"), I("mage_5", "mage_line_4_1407F0E540914B42"),
        I("bard_1", "bard_line_1_665D3DD481B4F2B1"), I("bard_2", "bard_line_2_9BC13B02244F2CCE"), I("bard_3", "bard_line_3_292F7849A4CD3D6D"),
        I("bard_4", "bard_line_4_EE862F9C6C2F489E"),
        I("healer_1", "healer_line_1_CAA08532FD6A5564"), I("healer_2", "healer_line_5_A81ECBD118FDBD03"), I("healer_3", "healer_line_2_F36BCFDF34ADEF01"),
        I("healer_4", "healer_line_3_2D469FC76BCE4EA2"), I("healer_5", "healer_line_4_937ABDCBE7D1B193"),
    };

    private static readonly Dictionary<string, string> ByName = new()
    {
        ["견습전사"] = "warrior_1", ["기사"] = "warrior_2", ["전사"] = "warrior_3", ["대검전사"] = "warrior_4", ["검술사"] = "warrior_5",
        ["견습궁수"] = "archer_1", ["궁수"] = "archer_2", ["석궁사수"] = "archer_3", ["장궁병"] = "archer_4",
        ["견습도적"] = "thief_1", ["도적"] = "thief_2", ["격투가"] = "thief_3", ["듀얼블레이드"] = "thief_4",
        ["견습마법사"] = "mage_1", ["전격술사"] = "mage_2", ["마법사"] = "mage_3", ["화염술사"] = "mage_4", ["빙결술사"] = "mage_5",
        ["견습음유시인"] = "bard_1", ["음유시인"] = "bard_2", ["댄서"] = "bard_3", ["악사"] = "bard_4",
        ["견습힐러"] = "healer_1", ["암흑술사"] = "healer_2", ["힐러"] = "healer_3", ["사제"] = "healer_4", ["수도사"] = "healer_5",
    };

    /// <summary>직업 이름(공백 무시)의 이미지 id. 모르는 직업이면 null.</summary>
    public static string? IdOf(string? job)
    {
        if (string.IsNullOrWhiteSpace(job)) return null;
        var name = new string(job.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return ByName.GetValueOrDefault(name);
    }
}
