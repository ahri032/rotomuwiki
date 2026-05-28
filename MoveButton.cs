using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Rotomu.Data;
using Rotomu.Battle;

/// <summary>
/// 기술 버튼 한 개의 UI를 자동으로 채우는 컴포넌트.
/// 
/// 사용법:
///   1. Move1Button GameObject에 이 컴포넌트 추가
///   2. Inspector에서 Text/InfoText/ResultText/Image 연결
///   3. SetMove("화염방사") 또는 SetBattle(공격자, 방어자) 호출
/// 
/// 데미지 계산:
///   - 공격자/방어자 정보가 있으면 자동으로 ResultText 채움
///   - 4가지 노력치 케이스 중 HB 풀보정 기준 (가장 흔한 사양)
///   - 결과: "자속 · 확정 2타 ▼" 같은 형태
/// 
/// 단계 3 추가:
///   - 클릭 시 옆 패널 토글 (4가지 노력치 케이스 KO 타수 표시)
///   - 연결선 표시 (버튼 ↔ 패널)
/// </summary>
public class MoveButton : MonoBehaviour
{
    [Header("기술 설정")]
    [Tooltip("표시할 기술 이름 (예: 화염방사)")]
    public string moveName = "화염방사";

    [Header("UI 참조 — Inspector에서 드래그로 연결")]
    public TMP_Text nameText;
    public TMP_Text infoText;
    public TMP_Text resultText;
    public Image backgroundImage;

    [Header("폰트 (선택)")]
    public TMP_FontAsset koreanFont;

    [Header("[단계 3] 클릭 처리")]
    [Tooltip("Button 컴포넌트 - 비어있으면 자동 검색")]
    public Button button;
    [Tooltip("MoveBoxManager 참조 - 비어있으면 자동 검색")]
    public MoveBoxManager manager;

    [Header("[단계 3] 강조 효과 (Shadow)")]
    [Tooltip("클릭 시 활성화될 Shadow 컴포넌트 (backgroundImage에 붙임)")]
    public UnityEngine.UI.Shadow highlightShadow;

    // === 전투 정보 (런타임 설정) ===
    private PokemonData _attacker;
    private PokemonData _defender;
    private CalcSide _attackerSide;
    private int _atkRank;
    private int _defRank;

    // === 내부 상태 ===
    private MoveData _data;
    private MoveEstimation _estimation;

    public MoveData Data => _data;
    public MoveEstimation Estimation => _estimation;

    // ==================================================================
    // 타입별 색상 (본체 TYPE_COLORS 그대로 이식)
    // ==================================================================
    private static readonly Dictionary<string, Color> TypeBackgrounds = new Dictionary<string, Color>
    {
        { "노말",   HexColor(0xA8, 0xA7, 0x7A, 0x66) },
        { "불꽃",   HexColor(0xEE, 0x81, 0x30, 0x66) },
        { "물",     HexColor(0x63, 0x90, 0xF0, 0x66) },
        { "풀",     HexColor(0x7A, 0xC7, 0x4C, 0x66) },
        { "전기",   HexColor(0xF7, 0xD0, 0x2C, 0x66) },
        { "얼음",   HexColor(0x96, 0xD9, 0xD6, 0x66) },
        { "격투",   HexColor(0xC2, 0x2E, 0x28, 0x66) },
        { "독",     HexColor(0xA3, 0x3E, 0xA1, 0x66) },
        { "땅",     HexColor(0xE2, 0xBF, 0x65, 0x66) },
        { "비행",   HexColor(0xA9, 0x8F, 0xF3, 0x66) },
        { "에스퍼", HexColor(0xF9, 0x55, 0x87, 0x66) },
        { "벌레",   HexColor(0xA6, 0xB9, 0x1A, 0x66) },
        { "바위",   HexColor(0xB6, 0xA1, 0x36, 0x66) },
        { "고스트", HexColor(0x73, 0x57, 0x97, 0x66) },
        { "드래곤", HexColor(0x6F, 0x35, 0xFC, 0x66) },
        { "악",     HexColor(0x70, 0x57, 0x46, 0x66) },
        { "강철",   HexColor(0xB7, 0xB7, 0xCE, 0x66) },
        { "페어리", HexColor(0xD6, 0x85, 0xAD, 0x66) },
    };

    private static Color HexColor(int r, int g, int b, int a)
    {
        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    void Awake()
    {
        // Button 자동 검색
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        // 클릭 이벤트 등록
        if (button != null)
        {
            button.onClick.AddListener(OnClick);
        }
        else
        {
            Debug.LogWarning($"[MoveButton] {gameObject.name}에 Button 컴포넌트가 없습니다. 클릭 작동 안 함.");
        }

        // 시작 시 강조 효과 꺼두기
        if (highlightShadow != null) highlightShadow.enabled = false;
    }

    void Start()
    {
        if (!DataLoader.I.IsInitialized)
        {
            DataLoader.I.Init();
        }

        // Manager 자동 검색 (Inspector에 안 넣은 경우)
        if (manager == null)
        {
            manager = GetComponentInParent<MoveBoxManager>();
        }

        if (!string.IsNullOrEmpty(moveName))
        {
            SetMove(moveName);
        }
    }

    /// <summary>
    /// 기술 이름으로 버튼 기본 정보 설정 (이름, 타입색, 위력 등).
    /// 데미지 계산은 SetBattle 호출 후 실행.
    /// </summary>
    public void SetMove(string moveName)
    {
        var move = DataLoader.I.GetMove(moveName);
        if (move == null)
        {
            Debug.LogWarning($"[MoveButton] 기술 못 찾음: {moveName}");
            if (nameText != null) nameText.text = moveName;
            return;
        }

        _data = move;
        this.moveName = moveName;

        UpdateName();
        UpdateInfo();
        UpdateBackground();
        UpdateFont();

        // 전투 정보가 이미 있으면 데미지도 갱신
        if (_attacker != null && _defender != null)
        {
            UpdateDamage();
        }
        else if (resultText != null)
        {
            resultText.text = "";
        }

        gameObject.name = $"Move_{moveName}";
    }

    /// <summary>
    /// 공격자/방어자 정보 설정. 자동으로 데미지 계산해서 ResultText 채움.
    /// 보통 MoveBoxManager가 4개 버튼에 한꺼번에 호출.
    /// </summary>
    public void SetBattle(
        PokemonData attacker, PokemonData defender,
        CalcSide attackerSide = null,
        int atkRank = 0, int defRank = 0)
    {
        _attacker = attacker;
        _defender = defender;
        _attackerSide = attackerSide ?? new CalcSide();
        _atkRank = atkRank;
        _defRank = defRank;

        if (_data != null)
        {
            UpdateDamage();
        }
    }

    // ==================================================================
    // [단계 3] 클릭 처리
    // ==================================================================

    /// <summary>
    /// 버튼 클릭 시 호출.
    /// MoveBoxManager에 클릭 알림 (실제 표시는 Manager가 단일 패널에 처리).
    /// </summary>
    public void OnClick()
    {
        // 변화기는 클릭 무시 (KO 타수 정보 없음)
        if (_data != null && _data.IsStatus)
        {
            Debug.Log($"[MoveButton] {moveName}: 변화기는 KO 타수 정보 없음");
            return;
        }

        if (manager != null)
        {
            manager.OnButtonClicked(this);
        }
        else
        {
            Debug.LogWarning($"[MoveButton] Manager 참조 없음 — 클릭 처리 불가");
        }
    }

    /// <summary>
    /// 강조 효과 켜기/끄기. Manager가 호출.
    /// </summary>
    public void SetHighlighted(bool on)
    {
        if (highlightShadow != null) highlightShadow.enabled = on;
    }

    // ==================================================================
    // 갱신 함수
    // ==================================================================

    private void UpdateName()
    {
        if (nameText == null || _data == null) return;
        nameText.text = _data.name;
    }

    private void UpdateInfo()
    {
        if (infoText == null || _data == null) return;

        if (_data.IsStatus)
        {
            infoText.text = $"{_data.type}·변화";
        }
        else
        {
            infoText.text = $"{_data.type}·{_data.cat}·{_data.Power}";
        }
    }

    private void UpdateBackground()
    {
        if (backgroundImage == null || _data == null) return;
        if (TypeBackgrounds.TryGetValue(_data.type, out var color))
        {
            backgroundImage.color = color;
        }
    }

    private void UpdateFont()
    {
        if (koreanFont == null) return;
        if (nameText != null) nameText.font = koreanFont;
        if (infoText != null) infoText.font = koreanFont;
        if (resultText != null) resultText.font = koreanFont;
    }

    private void UpdateDamage()
    {
        if (resultText == null || _data == null) return;

        // 변화기는 데미지 계산 X
        if (_data.IsStatus)
        {
            resultText.text = "변화기";
            return;
        }

        if (_attacker == null || _defender == null)
        {
            resultText.text = "";
            return;
        }

        // 4가지 노력치 케이스 계산
        _estimation = DamageEstimator.EstimateMove(
            _attacker, _defender, moveName,
            _attackerSide, _atkRank, _defRank);

        if (_estimation == null || _estimation.BestGuess == null)
        {
            resultText.text = "계산 불가";
            return;
        }

        var best = _estimation.BestGuess;

        // 무효 처리
        if (best.category == KOCategory.Immune)
        {
            resultText.text = "효과 없음";
            return;
        }

        // 자속 여부
        bool isStab = _attacker.type != null && _attacker.type.Contains(_data.type);
        string stabPrefix = isStab ? "자속 · " : "";

        // ResultText 채우기
        // 예: "자속 · 확정 2타  ▼"
        resultText.text = $"{stabPrefix}{best.koText}  ▼";
    }
}
