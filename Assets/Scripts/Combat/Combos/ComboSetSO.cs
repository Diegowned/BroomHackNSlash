using UnityEngine;

[CreateAssetMenu(fileName = "ComboSet", menuName = "Combat/Combo Set", order = 1)]
public class ComboSetSO : ScriptableObject
{
    [Header("Entry points")]
    public AttackStepSO lightStarter;
    public AttackStepSO mediumStarter;
}
