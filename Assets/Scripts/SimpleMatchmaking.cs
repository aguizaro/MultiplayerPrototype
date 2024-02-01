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

#if UNITY_EDITOR
using ParrelSync;
#endif

public class SimpleMatchmaking : MonoBehaviour
{
    [SerializeField] private GameObject _buttons;
    [SerializeField] private GameObject _gameMap;
    [SerializeField] private Transform mainCameraTransform;

    private Lobby _connectedLobby;
    private QueryResponse _lobbies;
    private const string JoinCodeKey = "j";
    private string _playerId;

    public async void CreateOrJoinLobby()
    {
        Debug.Log("Playing now... ");

        await Authenticate();

        Debug.Log("Auth Successfull");

        _connectedLobby = await QuickJoinLobby() ?? await CreateLobby();

        if (_connectedLobby == null)
        {
            Debug.LogError("Lobby Error: No Lobby connected");
            return;
        }

        _buttons.SetActive(false);
        Debug.Log("Connected lobby code: " + _connectedLobby.LobbyCode);
    }

    private async Task Authenticate()
    {
        var options = new InitializationOptions();

#if UNITY_EDITOR
        // Remove this if you don't have ParrelSync installed. 
        // It's used to differentiate the clients, otherwise lobby will count them as the same
        options.SetProfile(ClonesManager.IsClone() ? ClonesManager.GetArgument() : "Primary");
        Debug.Log("Profile: ");
        Debug.Log(options.ToString());
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

            Debug.Log("Found Lobby: " + lobby.Data);

            // If we found one, grab the relay allocation details
            var a = await RelayService.Instance.JoinAllocationAsync(lobby.Data[JoinCodeKey].Value);

            // configure unity tranport to use websockets for webGL support
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(a, "wss"));

            Debug.Log("Starting Client");

            // Initialize Game
            StartGame();

            // Join the game room as a client
            NetworkManager.Singleton.StartClient();
            return lobby;
        }
        catch (Exception)
        {
            Debug.Log($"No lobbies available via quick join");
            return null;
        }
    }

    private async Task<Lobby> CreateLobby()
    {
        try
        {
            const int maxPlayers = 100;

            // Create a relay allocation and generate a join code to share with the lobby
            var a = await RelayService.Instance.CreateAllocationAsync(maxPlayers);

            // configure unity tranport to use websockets for webGL support
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(a, "wss"));

            var joinCode = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);


            Debug.Log("Created Join Code: " + joinCode);

            // Create a lobby, adding the relay join code to the lobby data
            var options = new CreateLobbyOptions
            {
                Data = new Dictionary<string, DataObject> { { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) } }
            };
            var lobby = await Lobbies.Instance.CreateLobbyAsync("Useless Lobby Name", maxPlayers, options);

            Debug.Log("Created Lobby: " + lobby.Data);

            // Send a heartbeat every 15 seconds to keep the room alive
            StartCoroutine(HeartbeatLobbyCoroutine(lobby.Id, 15));

            Debug.Log("Started heartbeat routine");

            if (NetworkManager.Singleton.GetComponent<UnityTransport>() == null){
                Debug.Log("unity transport is null");
                return null;
            }

            Debug.Log("Starting Host");

            // Initialize Game
            StartGame();

            // Start the room. I'm doing this immediately, but maybe you want to wait for the lobby to fill up
            NetworkManager.Singleton.StartHost();

            Debug.Log(NetworkManager.Singleton.didStart);

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

    private void StartGame()
    {
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