using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10000)] // we usually want this to happen before anything else.. 
public class AutomaticallyUnparentChildren : MonoBehaviour
{
    private void OnEnable()
    {
        transform.DetachChildren();

        // destroy this component, its no longer needed. 
        Destroy(this);
    }
}
