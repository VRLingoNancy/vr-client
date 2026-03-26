using UnityEngine;
// using Oculus.Platform;
// using Oculus.Platform.Models;

public class MetaUserManager : MonoBehaviour
{
    // void Start()
    // {
    //     try
    //     {
    //         Core.Initialize();
    //         Debug.Log("Oculus Platform Initialized");

    //         Entitlements.IsUserEntitledToApplication().OnComplete(OnEntitlementCheck);
    //     }
    //     catch (UnityException e)
    //     {
    //         Debug.LogError("Platform init failed: " + e.Message);
    //     }
    // }

    // void OnEntitlementCheck(Message msg)
    // {
    //     if (msg.IsError)
    //     {
    //         Debug.LogError("User not entitled");
    //         Application.Quit();
    //         return;
    //     }

    //     Debug.Log("User entitled. Getting user info...");
    //     Users.GetLoggedInUser().OnComplete(OnGetUser);
    // }

    // void OnGetUser(Message<User> msg)
    // {
    //     if (msg.IsError)
    //     {
    //         Debug.LogError("Failed to get user");
    //         return;
    //     }

    //     User user = msg.Data;

    //     Debug.Log("User ID: " + user.ID);
    //     Debug.Log("User Name: " + user.OculusID);
    //     Debug.Log("Display Name: " + user.DisplayName);
    // }
}
