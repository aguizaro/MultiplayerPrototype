
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


    private NetworkManager _netManager;

    private UnityTransport _transport;
    private const int MaxPlayers = 5;

    private async void Awake()
    {
        _netManager = FindFirstObjectByType<NetworkManager>();
        _transport = FindFirstObjectByType<UnityTransport>();

        if (_netManager == null)
        {
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

        Debug.Log("Auth Successfull");
    }

    public async void CreateGame()
    {
        Debug.Log("Creating Game");

        _buttons.SetActive(false);

        Allocation a = await RelayService.Instance.CreateAllocationAsync(MaxPlayers);
        _joinCodeText.text = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);

        Debug.Log("Join code: " + _joinCodeText.text);

        _transport.SetHostRelayData(a.RelayServer.IpV4, (ushort)a.RelayServer.Port, a.AllocationIdBytes, a.Key, a.ConnectionData);

        Debug.Log("Starting Host: ");

        _netManager.StartHost();

        Debug.Log("Is Host: " + _netManager.IsHost);
        Debug.Log("Is Server: " + _netManager.IsServer);
        Debug.Log("Is Active: " + _netManager.isActiveAndEnabled);
    }

    public async void JoinGame()
    {
        _buttons.SetActive(false);

        Debug.Log(RelayService.Instance.ToString());

        Debug.Log("Joining");
        Debug.Log(_joinInput);

        JoinAllocation a = await RelayService.Instance.JoinAllocationAsync(_joinInput.text);

        Debug.Log("Setting Client Relay Data");

        _transport.SetClientRelayData(a.RelayServer.IpV4, (ushort)a.RelayServer.Port, a.AllocationIdBytes, a.Key, a.ConnectionData, a.HostConnectionData);

        Debug.Log("Starting client");

        _netManager.StartClient();

        Debug.Log("Is Client: " + _netManager.IsClient);
        Debug.Log("Is connected client: " + _netManager.IsConnectedClient);
        Debug.Log("Is active: " + _netManager.isActiveAndEnabled);
    }
}
