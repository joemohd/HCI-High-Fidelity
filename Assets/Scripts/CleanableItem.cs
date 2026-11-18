using UnityEngine;

public enum ItemCategory
{
    TrashNapkin,
    RecyclableCan,
    FoodCrumbSpill,
    ForgottenPersonalItem // e.g. Phone or wallet (requires human guidance!)
}

public class CleanableItem : MonoBehaviour
{
    public string itemName = "Napkin";
    public ItemCategory category = ItemCategory.TrashNapkin;
    public bool requiresHumanConfirmation = false;
    public string cleaningActionDescription = "Vacuuming";
    public bool isCleaned = false;

    public void CleanUp()
    {
        isCleaned = true;
        // Visual feedback: shrink or deactivate
        StartCoroutine(DisappearRoutine());
    }

    private System.Collections.IEnumerator DisappearRoutine()
    {
        Vector3 origScale = transform.localScale;
        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            transform.localScale = Vector3.Lerp(origScale, Vector3.zero, elapsed / 0.3f);
            elapsed += Time.deltaTime;
            yield return null;
        }
        gameObject.SetActive(false);
    }
}

