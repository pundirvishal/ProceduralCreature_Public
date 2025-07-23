using Unity.Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SceneCamera : MonoBehaviour
{
    [SerializeField] private CinemachineCamera cinemachineCamera;
    private Transform followObject;

    // Start is called before the first frame update
    void Start()
    {
        followObject = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (followObject != null)
        {
            cinemachineCamera.Target.TrackingTarget = followObject;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (followObject == null)
        {
            followObject = GameObject.FindGameObjectWithTag("Player")?.transform;
            cinemachineCamera.Target.TrackingTarget = followObject;
        }
    }
}
