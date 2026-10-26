using UnityEngine;

public class SwordBehaviur : MonoBehaviour
{
    [SerializeField] private MeshFilter _swordMesh;
    private SwordData _swordata;


    public void SetSword(SwordData sword)
    {
        _swordata = sword;
        _swordMesh.mesh = _swordata.Mesh;
    }
}
