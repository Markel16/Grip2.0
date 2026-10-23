using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TrackCarouselController : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TMP_Text selectedTrackName;
    [SerializeField] private string[] trackDisplayNames = { "Circuito 1" };
    [SerializeField] private string[] trackSceneNames = { "CIrcuito1" };
    [SerializeField, Min(0.05f)] private float snapDuration = 0.28f;

    private int selectedIndex;
    private Coroutine snapRoutine;

    public string SelectedSceneName
    {
        get
        {
            if (trackSceneNames == null || trackSceneNames.Length == 0)
            {
                return string.Empty;
            }

            int index = Mathf.Clamp(selectedIndex, 0, trackSceneNames.Length - 1);
            return trackSceneNames[index];
        }
    }

    private int TrackCount => trackSceneNames != null ? trackSceneNames.Length : 0;

    private void Awake()
    {
        if (scrollRect == null)
        {
            scrollRect = GetComponent<ScrollRect>();
        }

        if (scrollRect == null)
        {
            Debug.LogError("TrackCarouselController needs a ScrollRect reference.", this);
            enabled = false;
            return;
        }

        if (TrackCount == 0)
        {
            Debug.LogError("Add at least one scene name to the track carousel.", this);
            enabled = false;
            return;
        }

        if (trackDisplayNames == null || trackDisplayNames.Length != TrackCount)
        {
            Debug.LogError("Track display names and scene names must have the same length.", this);
            enabled = false;
            return;
        }

        if (scrollRect.content == null || scrollRect.content.childCount != TrackCount)
        {
            Debug.LogWarning("Create one carousel card under ScrollRect Content for each track scene.", this);
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, TrackCount - 1);
        UpdateSelectedTrackName();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (snapRoutine != null)
        {
            StopCoroutine(snapRoutine);
            snapRoutine = null;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (TrackCount < 2)
        {
            selectedIndex = 0;
            UpdateSelectedTrackName();
            return;
        }

        selectedIndex = Mathf.Clamp(
            Mathf.RoundToInt(scrollRect.horizontalNormalizedPosition * (TrackCount - 1)),
            0,
            TrackCount - 1);
        UpdateSelectedTrackName();
        SnapToSelectedTrack();
    }

    public void ShowPreviousTrack()
    {
        SelectTrack(selectedIndex - 1);
    }

    public void ShowNextTrack()
    {
        SelectTrack(selectedIndex + 1);
    }

    public void SelectTrack(int index)
    {
        if (TrackCount == 0)
        {
            return;
        }

        selectedIndex = Mathf.Clamp(index, 0, TrackCount - 1);
        UpdateSelectedTrackName();
        SnapToSelectedTrack();
    }

    private void SnapToSelectedTrack()
    {
        if (scrollRect == null || TrackCount < 2)
        {
            return;
        }

        if (snapRoutine != null)
        {
            StopCoroutine(snapRoutine);
        }

        snapRoutine = StartCoroutine(AnimateSnap());
    }

    private IEnumerator AnimateSnap()
    {
        float start = scrollRect.horizontalNormalizedPosition;
        float target = selectedIndex / (float)(TrackCount - 1);
        float elapsed = 0f;

        while (elapsed < snapDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / snapDuration);
            float easedProgress = progress * progress * (3f - 2f * progress);
            scrollRect.horizontalNormalizedPosition = Mathf.Lerp(start, target, easedProgress);
            yield return null;
        }

        scrollRect.horizontalNormalizedPosition = target;
        snapRoutine = null;
    }

    private void UpdateSelectedTrackName()
    {
        if (selectedTrackName != null && trackDisplayNames != null && selectedIndex < trackDisplayNames.Length)
        {
            selectedTrackName.text = trackDisplayNames[selectedIndex];
        }
    }
}