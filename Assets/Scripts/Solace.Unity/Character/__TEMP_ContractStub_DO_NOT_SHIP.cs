// TEMPORARY COMPILE-CHECK STUB — DELETE BEFORE MERGE.
// The real FoxRig/FoxAnimator is implemented by a sibling agent from the fox
// contract. This file exists ONLY so compile-check.sh can validate the rest
// of Solace.Unity against the real UnityEngine.dll. It mirrors the contract
// verbatim and must not ship.
using UnityEngine;

namespace Solace.Unity.Character
{
    public class FoxRig : MonoBehaviour
    {
        public static FoxRig Build(Transform parent, float scale = 1f) { return null; }
        public Transform Root, Body, Chest, Neck, Head, EarL, EarR;
        public Transform TailBase, TailMid, TailTip;
        public Transform LegFL_Upper, LegFL_Lower, LegFL_Paw, LegFR_Upper, LegFR_Lower, LegFR_Paw;
        public Transform LegBL_Upper, LegBL_Lower, LegBL_Paw, LegBR_Upper, LegBR_Lower, LegBR_Paw;
        public Transform ChestCore;
        public Light CoreLight;
        public void SetGlow(float glow) { }
    }

    public class FoxAnimator : MonoBehaviour
    {
        public enum Clip { Idle, Walk, Trot, Run, Pounce, Hurt, Sit, Sleep, PlayBow }
        public void Play(Clip clip, float fadeTime = 0.25f) { }
        public Clip Current { get { return Clip.Idle; } }
    }
}
