// code provided by https://gist.github.com/Matthew-J-Spencer/a5ab1fb5a50465e300ea39d7cde85006

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using Unity.Networking.Transport.Relay;
using TMPro;

#if UNITY_EDITOR
using ParrelSync;
#endif

public class SimpleMatchmaking : MonoBehaviour
{
    [SerializeField] private GameObject _buttons;
    [SerializeField] private TMP_InputField _inputField;
    [SerializeField] private TMP_Text _codeText;
    [SerializeField] private GameObject _gameMap;
    [SerializeField] private Transform mainCameraTransform;

    private QueryResponse _lobbies;
    private const string JoinCodeKey = "joinCodeRecord";
    private string _playerId;
    private Lobby _connectedLobby;

    public async void PlayButtonPressed()
    {
        try
        {
            Debug.Log("Playing now... ");

            await Authenticate();

            Debug.Log("Auth Successfull");

            _connectedLobby = await QuickJoinLobby() ?? await CreateLobby(6);

            if (_connectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "Lobby Error: No Lobby connected");

            _buttons.SetActive(false);

            Debug.Log("Connected lobby code: " + _connectedLobby.LobbyCode);

        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }
    }

    public async void JoinButtonPressed()
    {
        try
        {
            string joinCode = _inputField.text;
            Debug.Log("Looking for lobby with code: " + joinCode);

            await Authenticate();
            Debug.Log("Auth Successfull");

            _connectedLobby = await JoinLobbyCode(joinCode);

            if (_connectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "Lobby Error: No Lobby connected");

            _buttons.SetActive(false);

            Debug.Log("Connected lobby code: " + _connectedLobby.LobbyCode);
        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }

    }

    public async void CreateButtonPressed()
    {
        try
        {
            Debug.Log("Creating new Lobby");
            await Authenticate();
            Debug.Log("Auth Successfull");

            _connectedLobby = await CreateLobby(6);

            if (_connectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "Lobby Error: No Lobby connected");

            _buttons.SetActive(false);

            Debug.Log("Created lobby code: " + _connectedLobby.LobbyCode);
            _codeText.text = _connectedLobby.LobbyCode;
        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }
    }

    private async Task<Lobby> JoinLobbyCode(string joinCode)
    {
        try
        {
            _connectedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode);

            if (_connectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "No Lobby Found using code: " + joinCode);

            // If we found one, grab the relay allocation details
            var a = await RelayService.Instance.JoinAllocationAsync(_connectedLobby.Data[JoinCodeKey].Value);

            // configure unity tranport to use websockets for webGL support
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(a, "wss"));
            NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

            Debug.Log("Starting Client");

            //disable buttons:
            _buttons.SetActive(false);

            // Initialize Game
            StartGame(_connectedLobby);

            // Join the game room as a client
            NetworkManager.Singleton.StartClient();

            Debug.Log("is client: " + NetworkManager.Singleton.IsClient);
            Debug.Log("is server: " + NetworkManager.Singleton.IsServer);
            Debug.Log("connected clients: " + NetworkManager.Singleton.ConnectedClients.Count);
            for (int i=0; i< NetworkManager.Singleton.ConnectedClients.Count; i++)
            {
                Debug.Log("- clientID: " + NetworkManager.Singleton.ConnectedClientsIds[0]);
            }
            Debug.Log("connected hostname: " + NetworkManager.Singleton.ConnectedHostname);
            Debug.Log("Lobby Code: " + _connectedLobby.LobbyCode);
            Debug.Log("Lobby HostID: " + _connectedLobby.HostId);
            Debug.Log("Lobby Name: " + _connectedLobby.Name);
            Debug.Log("Lobby slots available: " + _connectedLobby.AvailableSlots);


            return _connectedLobby;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            return null;
        }
    }

    private async Task Authenticate()
    {
        var options = new InitializationOptions();

#if UNITY_EDITOR
        // Remove this if you don't have ParrelSync installed. 
        // It's used to differentiate the clients, otherwise lobby will count them as the same
        options.SetProfile(ClonesManager.IsClone() ? ClonesManager.GetArgument() : "Primary");
        Debug.Log("Profile: ");
        Debug.Log(ClonesManager.IsClone() ? ClonesManager.GetArgument() : "Primary");
        
#endif

        await UnityServices.InitializeAsync(options);

        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        _playerId = AuthenticationService.Instance.PlayerId;
    }

    private async Task<Lobby> QuickJoinLobby()
    {
        try
        {
            // Attempt to join a lobby in progress
            var lobby = await Lobbies.Instance.QuickJoinLobbyAsync();

            if (lobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "No Lobbies found");

            // If we found one, grab the relay allocation details
            var a = await RelayService.Instance.JoinAllocationAsync(lobby.Data[JoinCodeKey].Value);

            Debug.Log("Relay Service Instance Name: " + RelayService.Instance.GetType().Name);
            Debug.Log("Relay Service Instance string: " + RelayService.Instance.ToString());
            ;
            // configure unity tranport to use websockets for webGL support
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(a, "wss"));
            NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

            Debug.Log("Relay Service using websockets: " + NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets);

            Debug.Log("Starting Client");

            //disable buttons:
            _buttons.SetActive(false);

            // Initialize Game
            StartGame(lobby);

            // Join the game room as a client
            NetworkManager.Singleton.StartClient();

            Debug.Log("is client: " + NetworkManager.Singleton.IsClient);
            Debug.Log("is server: " + NetworkManager.Singleton.IsServer);
            Debug.Log("connected clients: " + NetworkManager.Singleton.ConnectedClients.Keys.ToString());
            Debug.Log("connected clientIDs: " + NetworkManager.Singleton.ConnectedClientsIds.ToString());
            Debug.Log("connected hostname: " + NetworkManager.Singleton.ConnectedHostname);
            Debug.Log("Lobby Code: " + lobby.LobbyCode);
            Debug.Log("Lobby HostID: " + lobby.HostId);
            Debug.Log("Lobby Name: " + lobby.Name);
            Debug.Log("Lobby slots available: " + lobby.AvailableSlots);
            Debug.Log("Lobby Data: " + lobby.Data.ToString());

            return lobby;
        }
        catch (Exception)
        {
            Debug.Log($"No lobbies available via quick join");
            return null;
        }
    }

    private async Task<Lobby> CreateLobby(int maxPlayers)
    {
        try
        {

            // Create a relay allocation and generate a join code to share with the lobby
            var a = await RelayService.Instance.CreateAllocationAsync(maxPlayers);

            // configure unity tranport to use websockets for webGL support
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(a, "wss"));
            NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

            var joinCode = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);
            Debug.Log("Created Join Code: " + joinCode);

            // Create a lobby, adding the relay join code to the lobby data
            var options = new CreateLobbyOptions
            {
                Data = new Dictionary<string, DataObject> { { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) } }
            };
            var lobby = await Lobbies.Instance.CreateLobbyAsync("MyLobbyName", maxPlayers, options);

            Debug.Log("Created public Lobby: " + lobby.ToString());

            // Send a heartbeat every 15 seconds to keep the room alive
            StartCoroutine(HeartbeatLobbyCoroutine(lobby.Id, 15));

            Debug.Log("Started heartbeat routine");

            if (NetworkManager.Singleton.GetComponent<UnityTransport>() == null){
                Debug.Log("unity transport is null");
                return null;
            }

            Debug.Log("Starting Host");

            //disable buttons:
            _buttons.SetActive(false);

            // Initialize Game
            StartGame(lobby);

            // Start the room. I'm doing this immediately, but maybe you want to wait for the lobby to fill up
            NetworkManager.Singleton.StartHost();

            Debug.Log("is host: " + NetworkManager.Singleton.IsHost);
            Debug.Log("is server: " + NetworkManager.Singleton.IsServer);
            Debug.Log("connected clients: " + NetworkManager.Singleton.ConnectedClients.Keys);
            Debug.Log("connected clientIDs: " + NetworkManager.Singleton.ConnectedClientsIds);
            Debug.Log("connected hostname: " + NetworkManager.Singleton.ConnectedHostname);
            Debug.Log("Lobby Code: " + lobby.LobbyCode);
            Debug.Log("Lobby HostID: " + lobby.HostId);
            Debug.Log("Lobby Name: " + lobby.Name);
            Debug.Log("Lobby slots available: " + lobby.AvailableSlots);
            Debug.Log("Lobby Data: " + lobby.Data.ToString());


            return lobby;
        }
        catch (Exception)
        {
            Debug.LogFormat("Failed creating a lobby");
            return null;
        }
    }

    private static IEnumerator HeartbeatLobbyCoroutine(string lobbyId, float waitTimeSeconds)
    {
        var delay = new WaitForSecondsRealtime(waitTimeSeconds);
        while (true)
        {
            Lobbies.Instance.SendHeartbeatPingAsync(lobbyId);
            yield return delay;
        }
    }

    private void StartGame(Lobby connectedLobby)
    {
        _codeText.text = connectedLobby.LobbyCode;
        Instantiate(_gameMap);
        SetCameraTransform(new Vector3(0f, 13.62f, 0), new Vector3(90f, 0, 0), Vector3.one);
    }

    private void SetCameraTransform(Vector3 position, Vector3 rotationEulerAngles, Vector3 scale)
    {
        mainCameraTransform.position = position;
        mainCameraTransform.rotation = Quaternion.Euler(rotationEulerAngles);
        mainCameraTransform.localScale = scale;
    }

    private void OnDestroy()
    {
        try
        {
            StopAllCoroutines();
            // todo: Add a check to see if you're host
            if (_connectedLobby != null)
            {
                if (_connectedLobby.HostId == _playerId) Lobbies.Instance.DeleteLobbyAsync(_connectedLobby.Id);
                else Lobbies.Instance.RemovePlayerAsync(_connectedLobby.Id, _playerId);
            }
        }
        catch (Exception e)
        {
            Debug.Log($"Error shutting down lobby: {e}");
        }
    }
}