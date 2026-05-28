using UnityEngine;
using TMPro;
using Rotomu.Data;
using Rotomu.Battle;

/// <summary>
/// 4개의 MoveButton을 통합 관리하는 매니저.
/// MoveBox GameObject에 붙어서, 자식 MoveButton 4개를 한 번에 제어.
/// 
/// 사용법:
///   1. MoveBox GameObject에 이 컴포넌트 추가
///   2. Inspector에서 PlayerSlot, EnemySlot 연결
///   3. 자동으로 PlayerSlot의 기술 4개를 각 MoveButton에 배치
///   4. 데미지 계산 결과도 자동 채움
/// 
/// 단계 3 (단일 패널 + ㄷ자 경로선):
///   - 공통 MovePanel 1개 (MoveBox 오른쪽 고정 위치)
///   - 기술별 PathLine 4개 (ㄷ자 형태, 미리 Unity에서 배치)
///   - 버튼 클릭 → 패널 텍스트 갱신 + 해당 ㄷ자 경로선만 활성화 + 버튼 강조
///   - 같은 버튼 다시 클릭 → 패널/선/강조 모두 해제 (토글)
/// </summary>
public class MoveBoxManager : MonoBehaviour
{
    [Header("전투 슬롯 참조 — Inspector에서 드래그로 연결")]
    [Tooltip("공격자 (내 포켓몬)")]
    public BattleSlot playerSlot;

    [Tooltip("방어자 (상대 포켓몬)")]
    public BattleSlot enemySlot;

    [Header("기술 버튼들 — 자동 검색 또는 직접 연결")]
    [Tooltip("비워두면 자식에서 자동 검색")]
    public MoveButton[] moveButtons;

    [Header("[단계 3] 단일 패널·라인")]
    [Tooltip("4가지 노력치 케이스를 표시하는 공통 패널")]
    public GameObject movePanel;

    [Tooltip("패널 안의 4가지 케이스 텍스트 (none/H풀/HB풀/HD풀 순서)")]
    public TMP_Text noneCaseText;
    public TMP_Text hFullCaseText;
    public TMP_Text hbFullCaseText;
    public TMP_Text hdFullCaseText;

    [Tooltip("선택사항: 패널 상단에 '○○○ 기술의 KO 타수' 표시")]
    public TMP_Text panelTitleText;

    [Tooltip("각 MoveButton에 대응하는 ㄷ자 경로 GameObject 배열 (4개). 순서는 moveButtons와 일치)")]
    public GameObject[] pathLines;

    [Header("폰트 (선택)")]
    public TMP_FontAsset koreanFont;

    // === [단계 3] 현재 열린 버튼 추적 ===
    private MoveButton _currentlyOpenButton = null;

    void Start()
    {
        if (!DataLoader.I.IsInitialized)
        {
            DataLoader.I.Init();
        }

        // 버튼 자동 검색 (Inspector에 안 넣은 경우)
        if (moveButtons == null || moveButtons.Length == 0)
        {
            moveButtons = GetComponentsInChildren<MoveButton>();
            Debug.Log($"[MoveBoxManager] 자식 MoveButton 자동 검색: {moveButtons.Length}개");
        }

        // 폰트 + Manager 자동 전달
        foreach (var btn in moveButtons)
        {
            if (btn == null) continue;
            if (koreanFont != null && btn.koreanFont == null)
            {
                btn.koreanFont = koreanFont;
            }
            // Manager 역참조 설정 (Inspector에 안 넣은 경우)
            if (btn.manager == null)
            {
                btn.manager = this;
            }
        }

        // 패널 + 모든 경로선 초기 상태: 숨김
        if (movePanel != null) movePanel.SetActive(false);
        HideAllPathLines();

        // 슬롯이 있으면 자동 설정
        if (playerSlot != null && enemySlot != null)
        {
            // BattleSlot의 Start()가 먼저 실행되도록 잠시 대기
            Invoke(nameof(UpdateAll), 0.1f);
        }
    }

    /// <summary>
    /// 4개 버튼 모두 갱신.
    /// PlayerSlot의 포켓몬 기술 4개를 각 버튼에 배치하고,
    /// EnemySlot 포켓몬을 방어자로 데미지 계산.
    /// </summary>
    public void UpdateAll()
    {
        if (playerSlot == null || enemySlot == null)
        {
            Debug.LogWarning("[MoveBoxManager] 슬롯이 설정되지 않음");
            return;
        }

        var attacker = playerSlot.Data;
        var defender = enemySlot.Data;

        if (attacker == null || defender == null)
        {
            Debug.LogWarning("[MoveBoxManager] 슬롯에 포켓몬 데이터 없음");
            return;
        }

        var moves = attacker.moves;
        if (moves == null || moves.Count == 0)
        {
            Debug.LogWarning($"[MoveBoxManager] {attacker.name}의 기술 목록이 비어있음");
            return;
        }

        // 각 버튼에 기술 배치
        for (int i = 0; i < moveButtons.Length; i++)
        {
            if (moveButtons[i] == null) continue;

            if (i < moves.Count)
            {
                moveButtons[i].SetMove(moves[i]);
                moveButtons[i].SetBattle(attacker, defender);
            }
            else
            {
                moveButtons[i].SetMove("");
            }
        }

        // 갱신되면 열린 패널은 일단 닫기 (잘못된 정보 방지)
        ClosePanel();

        Debug.Log($"[MoveBoxManager] 갱신 완료: {attacker.name} vs {defender.name}");
    }

    /// <summary>
    /// 새로운 슬롯 조합으로 갱신.
    /// </summary>
    public void SetBattle(BattleSlot newPlayerSlot, BattleSlot newEnemySlot)
    {
        playerSlot = newPlayerSlot;
        enemySlot = newEnemySlot;
        UpdateAll();
    }

    // ==================================================================
    // [단계 3] 단일 패널 관리
    // ==================================================================

    /// <summary>
    /// MoveButton이 클릭됐을 때 호출.
    ///   - 같은 버튼 다시 클릭 → 패널 닫기 (토글)
    ///   - 다른 버튼 클릭 → 텍스트 갱신 + 연결선 그 버튼 방향으로 회전
    /// </summary>
    public void OnButtonClicked(MoveButton clicked)
    {
        if (clicked == null) return;

        // 같은 버튼을 다시 클릭 → 닫기
        if (_currentlyOpenButton == clicked)
        {
            ClosePanel();
            return;
        }

        // 새 버튼 정보로 패널·라인 갱신
        OpenPanelFor(clicked);
    }

    /// <summary>
    /// 패널을 특정 버튼의 정보로 채우고 표시.
    /// + 해당 버튼 강조 + 해당 경로선 활성화
    /// </summary>
    private void OpenPanelFor(MoveButton btn)
    {
        if (btn == null) return;

        // 이전 버튼 강조 해제
        if (_currentlyOpenButton != null && _currentlyOpenButton != btn)
        {
            _currentlyOpenButton.SetHighlighted(false);
        }

        _currentlyOpenButton = btn;

        // 패널 활성화
        if (movePanel != null) movePanel.SetActive(true);

        // 새 버튼 강조
        btn.SetHighlighted(true);

        // 클릭한 버튼의 인덱스를 찾아서 해당 경로선만 활성화
        int btnIndex = System.Array.IndexOf(moveButtons, btn);
        ShowPathLine(btnIndex);

        // 텍스트 갱신
        UpdatePanelTexts(btn);
    }

    /// <summary>
    /// 패널 닫기 + 모든 강조·경로선 해제.
    /// </summary>
    public void ClosePanel()
    {
        if (movePanel != null) movePanel.SetActive(false);

        // 강조 해제
        if (_currentlyOpenButton != null)
        {
            _currentlyOpenButton.SetHighlighted(false);
        }

        // 모든 경로선 끄기
        HideAllPathLines();

        _currentlyOpenButton = null;
    }

    /// <summary>
    /// 특정 인덱스의 경로선만 활성화, 나머지는 끔.
    /// </summary>
    private void ShowPathLine(int buttonIndex)
    {
        if (pathLines == null) return;

        for (int i = 0; i < pathLines.Length; i++)
        {
            if (pathLines[i] != null)
            {
                pathLines[i].SetActive(i == buttonIndex);
            }
        }
    }

    /// <summary>
    /// 모든 경로선 끄기.
    /// </summary>
    private void HideAllPathLines()
    {
        if (pathLines == null) return;

        foreach (var line in pathLines)
        {
            if (line != null) line.SetActive(false);
        }
    }

    /// <summary>
    /// 패널의 4가지 케이스 텍스트를 클릭된 버튼 정보로 갱신.
    /// </summary>
    private void UpdatePanelTexts(MoveButton btn)
    {
        var est = btn.Estimation;

        // 타이틀 (선택사항)
        if (panelTitleText != null)
        {
            if (koreanFont != null) panelTitleText.font = koreanFont;
            panelTitleText.text = btn.moveName;
        }

        if (est == null)
        {
            SetCaseText(noneCaseText,   "무보정",  null);
            SetCaseText(hFullCaseText,  "H 풀",    null);
            SetCaseText(hbFullCaseText, "HB 풀",   null);
            SetCaseText(hdFullCaseText, "HD 풀",   null);
            return;
        }

        SetCaseText(noneCaseText,   "무보정",  est.none);
        SetCaseText(hFullCaseText,  "H 풀",    est.hFull);
        SetCaseText(hbFullCaseText, "HB 풀",   est.hbFull);
        SetCaseText(hdFullCaseText, "HD 풀",   est.hdFull);
    }

    /// <summary>
    /// 한 케이스의 텍스트 표시.
    /// </summary>
    private void SetCaseText(TMP_Text label, string caseName, KOEstimation est)
    {
        if (label == null) return;

        if (koreanFont != null) label.font = koreanFont;

        if (est == null)
        {
            label.text = $"{caseName}: -";
            return;
        }

        // 무효 처리
        if (est.category == KOCategory.Immune)
        {
            label.text = $"{caseName}: 효과 없음";
            return;
        }

        label.text = $"{caseName}: {est.koText}";
    }
}
