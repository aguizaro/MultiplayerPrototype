
// Code provided by https://gist.github.com/Matthew-J-Spencer/a5ab1fb5a50465e300ea39d7cde85006

using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable SuggestVarOrType_BuiltInTypes

public class RelayManager : MonoBehaviour
{
    [SerializeField] private GameObject _buttons;
    [SerializeField] private TMP_Text _joinCodeText;
    [SerializeField] private TMP_InputField _joinInput;
    [SerializeField] private GameObject _gameMap;
    [SerializeField] private Transform mainCameraTransform;

    private const int MaxPlayers = 5;

    private async void Awake()
    {
        _buttons.SetActive(false);

        await Authenticate();

        _buttons.SetActive(true);
    }

    private static async Task Authenticate()
    {
        await UnityServices.InitializeAsync();
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public async void CreateGame()
    {
        _buttons.SetActive(false);

        Allocation a = await RelayService.Instance.CreateAllocationAsync(MaxPlayers);
        _joinCodeText.text = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);

        // configure unity tranport to use websockets for webGL support
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(a, "wss"));
        NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

        Debug.Log("Join code: " + _joinCodeText.text);

        Instantiate(_gameMap);
        SetCameraTransform(new Vector3(0f, 13.62f, 0), new Vector3(90f,0,0), Vector3.one);

        NetworkManager.Singleton.StartHost();
    }

    public async void JoinGame()
    {
        if (_joinInput.text.Length <= 0)
        {
            _joinCodeText.text = "Code Not Entered";
            return;
        }

        _buttons.SetActive(false);

        JoinAllocation a = await RelayService.Instance.JoinAllocationAsync(_joinInput.text);

        // configure unity tranport to use websockets for webGL support
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(a, "wss"));
        NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

        if (a == null)
        {
            _joinCodeText.text = "Code Not Valid";
            Debug.LogError("Code not Valid");
            return;
        }

        _joinCodeText.text = _joinInput.text;

        Instantiate(_gameMap);
        SetCameraTransform(new Vector3(0f, 13.62f, 0), new Vector3(90f, 0, 0), Vector3.one);

        NetworkManager.Singleton.StartHost();
    }

    public void SetCameraTransform(Vector3 position, Vector3 rotationEulerAngles, Vector3 scale)
    {
        mainCameraTransform.position = position;
        mainCameraTransform.rotation = Quaternion.Euler(rotationEulerAngles);
        mainCameraTransform.localScale = scale;
    }
}
