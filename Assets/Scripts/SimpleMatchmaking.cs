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


public enum EncryptionType{DTLS, WSS}

public class SimpleMatchmaking : MonoBehaviour
{
    [SerializeField] private GameObject _buttons;
    [SerializeField] private TMP_InputField _inputField;
    [SerializeField] private TMP_Text _codeText;
    [SerializeField] private TMP_Text _lobbyDebug;
    [SerializeField] private TMP_Text _NetworkDebug;
    [SerializeField] private GameObject _gameMap;
    [SerializeField] private Transform mainCameraTransform;

    [SerializeField] EncryptionType encryption = EncryptionType.WSS;
    [SerializeField] int maxPlayers = 10;

    private const string RelayJoinCodeKey = "RelayJoinCode";
    private string _playerId;
    private Lobby _connectedLobby;
    private GameObject _currentMapInstance;
    private string _encrptionType => (encryption == EncryptionType.DTLS) ? "dtls": "wss";




    // Authentication --------------------------------------------------------------------------------------------------------------
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

    // Play Now --------------------------------------------------------------------------------------------------------------
    public async void PlayButtonPressed()
    {
        try
        {
            Debug.Log("Playing now... ");

            await Authenticate();

            Debug.Log("Auth Successfull");

            _connectedLobby = await TryQuick() ?? await CreateLobby(maxPlayers);

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

    // Attempts to quick join a lobby and return the lobby
    // null is returned if no lobbies are available for quick join
    private async Task<Lobby> TryQuick()
    {
        await QuickJoinLobby();
        if (_connectedLobby == null || !NetworkManager.Singleton.IsClient) return null;

        // Initialize Game
        StartGame(_connectedLobby);

        return _connectedLobby;
    }



    // Join --------------------------------------------------------------------------------------------------------------
    public async void JoinButtonPressed()
    {
        try
        {
            string joinCode = _inputField.text;
            Debug.Log("Looking for lobby with code: " + joinCode);

            await Authenticate();
            Debug.Log("Auth Successfull");

            await JoinGame(joinCode);

            if (_connectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "Lobby Error: No Lobby connected");

            _buttons.SetActive(false);

            Debug.Log("Connected lobby code: " + _connectedLobby.LobbyCode);
        }
        catch (Exception e)
        {
            Debug.Log($"Failed to join lobby: {e.Message}");
        }

    }
    // join lobby using code and start game
    private async Task JoinGame(string joinCode)
    {
        try
        {
            _connectedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode);

            if (_connectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "No Lobby Found using code: " + joinCode);

            // If we found one, grab the relay allocation details

            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(_connectedLobby.Data[RelayJoinCodeKey].Value);
 
            // configure unity tranport to use websockets for webGL support
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(allocation, _encrptionType));
            NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

            // Initialize Game
            StartGame(_connectedLobby);

            Debug.Log("Starting Client");
            // Join the game room as a client
            NetworkManager.Singleton.StartClient();

            

        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"Failed to join lobby: {e.Message}");
        }
    }

    // attempts quick joins a lobby and starts client
    // _connectedLobby is set to lobby, or null if it fails
    private async Task QuickJoinLobby()
    {
        try
        {
            _connectedLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
            StartCoroutine(PullUpdatesCoroutine(_connectedLobby.Id, 1));

            string relayJoinCode = _connectedLobby.Data[RelayJoinCodeKey].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(joinAllocation, _encrptionType));
            NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

            NetworkManager.Singleton.StartClient();

        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"Failed to quick join lobby: {e.Message}");
            _connectedLobby = null;
        }
    }


    // Create --------------------------------------------------------------------------------------------------------------
    public async void CreateButtonPressed()
    {
        try
        {
            Debug.Log("Creating new Lobby");
            await Authenticate();
            Debug.Log("Auth Successfull");

            Lobby createdLobby = await CreateLobby(maxPlayers);

            if (createdLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "Lobby Error: No Lobby connected");

            _buttons.SetActive(false);

            Debug.Log("Created lobby code: " + createdLobby.LobbyCode);
            _codeText.text = createdLobby.LobbyCode;
        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }
    }


    async Task<Lobby> CreateLobby(int maxPlayers)
    {
        try
        {

            // Create a relay allocation and generate a join code to share with the lobby
            Allocation allocation = await AllocateRelay(maxPlayers);

            // configure unity tranport to use websockets for webGL support
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(allocation, "wss"));
            NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

            string relayJoinCode = await GetRelayJoinCode(allocation);
            Debug.Log("Created Join Code: " + relayJoinCode);

            // Lobby options for a public lobby
            var options = new CreateLobbyOptions
            {   // add join code as a public (anyone can grab this code)
                IsPrivate = false,
                Data = new Dictionary<string, DataObject> { { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode) } }
            };
            var currentLobby = await Lobbies.Instance.CreateLobbyAsync("MyLobbyName", maxPlayers, options);

            Debug.Log("Created public Lobby: " + currentLobby.Created);

            // Send a heartbeat every 15 seconds to keep the room alive
            StartCoroutine(HeartbeatLobbyCoroutine(currentLobby.Id, 15));

            // Pull updates from the lobby every second
            StartCoroutine(PullUpdatesCoroutine(currentLobby.Id, 1));

            Debug.Log("Started host");

            // Start the room. I'm doing this immediately, but maybe you want to wait for the lobby to fill up
            NetworkManager.Singleton.StartHost();

            // Initialize Game
            StartGame(currentLobby);

            return currentLobby;
        }
        catch (Exception)
        {
            Debug.LogFormat("Failed creating a lobby");
            return null;
        }
    }


    // Allocation --------------------------------------------------------------------------------------------------------------

    async Task<Allocation> AllocateRelay(int maxPlayers)
    {
        try
        {
            return await RelayService.Instance.CreateAllocationAsync(maxPlayers);
        }
        catch(RelayServiceException e)
        {
            Debug.LogError($"Error allocating relay: {e.Message}");
            return null;
        }
    }


    // Relay --------------------------------------------------------------------------------------------------------------

    async Task<string> GetRelayJoinCode(Allocation allocation)
    {
        try
        {
            return await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Error getting relay join code: {e.Message}");
            return null;
        }
    }

    async Task<JoinAllocation> JoinRelay(string joinCode)
    {
        try
        {
            return await RelayService.Instance.JoinAllocationAsync(joinCode);
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Error joining relay: {e.Message}");
            return default;
        }
    }

    // Coroutines --------------------------------------------------------------------------------------------------------------


    private static IEnumerator HeartbeatLobbyCoroutine(string lobbyId, float waitTimeSeconds)
    {
        var delay = new WaitForSecondsRealtime(waitTimeSeconds);
        while (true)
        {
            Lobbies.Instance.SendHeartbeatPingAsync(lobbyId);
            yield return delay;
        }
    }

    private static IEnumerator PullUpdatesCoroutine(string lobbyId, float waitTimeSeconds)
    {
        var delay = new WaitForSecondsRealtime(waitTimeSeconds);
        while (true)
        {
            LobbyService.Instance.GetLobbyAsync(lobbyId);
            yield return delay;
        }
    }


    // Gameplay --------------------------------------------------------------------------------------------------------------

    private void StartGame(Lobby connectedLobby)
    {
        Debug.Log("STARTING GAME");

        //disable buttons:
        _buttons.SetActive(false);

        // show debug
        DebugOnScreen(_connectedLobby, _lobbyDebug, _NetworkDebug);

        _codeText.text = connectedLobby.LobbyCode;
        _currentMapInstance = Instantiate(_gameMap);
        SetCameraTransform(new Vector3(0f, 13.62f, 0), new Vector3(90f, 0, 0), Vector3.one);

        Debug.Log("current map: " + _currentMapInstance);
    }

    private void EndGame(Lobby connectedLobby)
    {
        _codeText.text = "";
        if (_currentMapInstance != null ) Destroy(_currentMapInstance);
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


    // Debug --------------------------------------------------------------------------------------------------------------

    private void DebugOnScreen(Lobby lobby, TMP_Text lobbyText, TMP_Text netText)
    {
        if (netText == null) { Debug.Log("Input Text: Network text is invalud"); return; }
        if (lobbyText == null) { Debug.Log("Input Text: lobby text is invalud"); return; }
        if (lobby == null) { Debug.Log("Lobby is null"); return; }

        string lobbyState = "----- Lobby Debug ------";
        lobbyState += "\nLobby Name: " + lobby.Name;
        lobbyState += "\nLobby Code: " + lobby.LobbyCode;
        lobbyState += "\nLobby HostID: " + lobby.HostId;
        lobbyState += "\nLast updated: " + lobby.LastUpdated;
        lobbyState += "\nLobby isLocked: " + lobby.IsLocked;
        lobbyState += "\nLobby isPrivate: " + lobby.IsPrivate;
        lobbyState += "\nMax Players: " + lobby.MaxPlayers;
        lobbyState += "\nLobby slots available: " + lobby.AvailableSlots;
        lobbyState += "\nLobby Players: " + lobby.Players.Count;
        for (int i = 0; i < lobby.Players.Count; i++)
        {
            lobbyState += "- Player: " + lobby.Players[i];
        }

        if (NetworkManager.Singleton.IsHost)
        {

            string networkState = "----- Network Manager Debug ------";
            networkState += "\nis host: " + NetworkManager.Singleton.IsHost;
            networkState += "\nis client: " + NetworkManager.Singleton.IsClient;
            networkState += "\nis server: " + NetworkManager.Singleton.IsServer;
            networkState += "\nis connectedClient: " + NetworkManager.Singleton.IsConnectedClient;
            networkState += "\nis approved: " + NetworkManager.Singleton.IsApproved;
            networkState += "\nconnected clients: " + NetworkManager.Singleton.ConnectedClients.Count;
            for (int i = 0; i < NetworkManager.Singleton.ConnectedClients.Count; i++)
            {
                networkState += "\n- clientID: " + NetworkManager.Singleton.ConnectedClientsIds[i];
            }

            lobbyText.text = lobbyState;
            netText.text = networkState;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) DebugOnScreen(_connectedLobby, _lobbyDebug, _NetworkDebug);
    }
}