
// Code provided by https://gist.github.com/Matthew-J-Spencer/a5ab1fb5a50465e300ea39d7cde85006

using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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


    private NetworkManager _netManager;

    private UnityTransport _transport;
    private const int MaxPlayers = 5;

    private async void Awake()
    {
        _netManager = FindFirstObjectByType<NetworkManager>();
        _transport = FindFirstObjectByType<UnityTransport>();

        if (_netManager == null)
        {
            Debug.Log("Setting _netManager");
            _netManager.SetSingleton();
            if (_netManager == null)
            {
                Debug.Log("Still null: ", _netManager);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Debug.Log(_netManager);
        }



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

        Debug.Log("Join code: " + _joinCodeText.text);

        _transport.SetHostRelayData(a.RelayServer.IpV4, (ushort)a.RelayServer.Port, a.AllocationIdBytes, a.Key, a.ConnectionData);

        Instantiate(_gameMap);
        SetCameraTransform(new Vector3(0f, 13.62f, 0), new Vector3(90f,0,0), Vector3.one);
        _netManager.StartHost();
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

        if (a == null)
        {
            _joinCodeText.text = "Code Not Valid";
            return;
        }

        _joinCodeText.text = _joinInput.text;

        _transport.SetClientRelayData(a.RelayServer.IpV4, (ushort)a.RelayServer.Port, a.AllocationIdBytes, a.Key, a.ConnectionData, a.HostConnectionData);

        Instantiate(_gameMap);
        SetCameraTransform(new Vector3(0f, 13.62f, 0), new Vector3(90f, 0, 0), Vector3.one);
        _netManager.StartClient();
    }

    public void SetCameraTransform(Vector3 position, Vector3 rotationEulerAngles, Vector3 scale)
    {
        mainCameraTransform.position = position;
        mainCameraTransform.rotation = Quaternion.Euler(rotationEulerAngles);
        mainCameraTransform.localScale = scale;
    }
}
