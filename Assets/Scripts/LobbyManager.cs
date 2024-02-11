//  References:
//  https://gist.github.com/Matthew-J-Spencer/a5ab1fb5a50465e300ea39d7cde85006
//  https://github.com/adammyhre/Unity-Multiplayer-Kart

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

public class LobbyEntry
{
    public string Name;
    public int SpotsAvailable;

    public LobbyEntry(string name, int numSpots)
    {
        Name = name;
        SpotsAvailable = numSpots;
    }
}


public enum EncryptionType{DTLS, WSS}

public class LobbyManager : MonoBehaviour
{
    [SerializeField] private GameObject _gameMap;
    [SerializeField] private Transform mainCameraTransform;
    [SerializeField] private UIManager _UIManager;

    [SerializeField] EncryptionType encryption = EncryptionType.WSS;
    [SerializeField] int maxLobbySize = 10;

    private const string RelayJoinCodeKey = "RelayJoinCode";
    private string _playerId;
    public Lobby ConnectedLobby;
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

    // Query Lobbies --------------------------------------------------------------------------------------------------------------

    public async Task<List<LobbyEntry>> FindOpenLobbies()
    {
        try
        {
            await Authenticate();

            QueryLobbiesOptions options = new QueryLobbiesOptions();
            options.Count = 25; //max results to return

            // filter for open lobbies only
            options.Filters = new List<QueryFilter>()
            {
                new QueryFilter(
                    field: QueryFilter.FieldOptions.AvailableSlots,
                    op: QueryFilter.OpOptions.GT,
                    value: "0")
            };

            // order by newest lobbies first
            options.Order = new List<QueryOrder>()
            {
                new QueryOrder(
                    asc: false,
                    field: QueryOrder.FieldOptions.Created)
            };

            QueryResponse lobbies = await Lobbies.Instance.QueryLobbiesAsync(options);

            Debug.Log($"lobby response: count: {lobbies.Results.Count}");

            List<LobbyEntry> _foundLobbies = new List<LobbyEntry>();
            foreach (Lobby found in lobbies.Results) {
                Debug.Log($"Found:\nName: {found.Name}\n  Code: {found.LobbyCode}\n  Available Slots: {found.AvailableSlots}\n Host:{found.HostId}");
                _foundLobbies.Add(new LobbyEntry(found.Name, found.AvailableSlots));
                foreach (Player p in found.Players) Debug.Log($"Player\n  ID: {p.Id}");
            }

            return _foundLobbies;

        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"Error finding open lobbies: {e}");
            return null;
        }
    }



    // Play Now --------------------------------------------------------------------------------------------------------------
    public async void PlayNow()
    {
        try
        {
            Debug.Log("Playing now... ");

            await Authenticate();

            Debug.Log("Auth Successfull");

            string defaultName = "QuickLobby " + (DateTime.Now).ToString("MMdd_HHmmss");
            ConnectedLobby = await TryQuick() ?? await CreateLobby(defaultName, maxLobbySize);

            if (ConnectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "Lobby Error: No Lobby connected");

            _UIManager.DeactivateUI();

            Debug.Log("Connected lobby code: " + ConnectedLobby.LobbyCode);

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
        if (ConnectedLobby == null || !NetworkManager.Singleton.IsClient) return null;

        // Initialize Game
        StartGame();

        return ConnectedLobby;
    }



    // Join --------------------------------------------------------------------------------------------------------------
    public async void Join(string joinCode)
    {
        try
        {
            Debug.Log("Looking for lobby with code: " + joinCode);

            await Authenticate();
            Debug.Log("Auth Successfull");

            await JoinGame(joinCode);

            if (ConnectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "Lobby Error: No Lobby connected");

            _UIManager.DeactivateUI();

            Debug.Log("Connected lobby code: " + ConnectedLobby.LobbyCode);
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
            ConnectedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode);

            if (ConnectedLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "No Lobby Found using code: " + joinCode);

            // If we found one, grab the relay allocation details

            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(ConnectedLobby.Data[RelayJoinCodeKey].Value);
 
            // configure unity tranport to use websockets for webGL support
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(allocation, _encrptionType));
            NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

            // Initialize Game
            StartGame();

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
            ConnectedLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
            StartCoroutine(PullUpdatesCoroutine(ConnectedLobby.Id, 1));

            string relayJoinCode = ConnectedLobby.Data[RelayJoinCodeKey].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(joinAllocation, _encrptionType));
            NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;

            NetworkManager.Singleton.StartClient();

        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"Failed to quick join lobby: {e.Message}");
            ConnectedLobby = null;
        }
    }


    // Create --------------------------------------------------------------------------------------------------------------
    public async void Create(string lobbyName, int lobbySize)
    {
        try
        {
            Debug.Log("Creating new Lobby");
            await Authenticate();
            Debug.Log("Auth Successfull");

            Lobby createdLobby = await CreateLobby(lobbyName, lobbySize);

            if (createdLobby == null) throw new LobbyServiceException(new LobbyExceptionReason(), "Lobby Error: No Lobby connected");

            _UIManager.DeactivateUI();

            Debug.Log("Created lobby code: " + createdLobby.LobbyCode);
            _UIManager.DisplayCode(createdLobby.LobbyCode);
        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }
    }


    async Task<Lobby> CreateLobby(string lobbyName, int maxPlayers)
    {
        try
        {

            // Create a relay allocation and generate a join code to share with the lobby
            Allocation allocation = await AllocateRelay(Math.Min(maxLobbySize, maxPlayers));

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

            string defaultName = "MyLobby " + (DateTime.Now).ToString("MMdd_HHmmss");
            string name = (lobbyName != null && lobbyName.Length > 0) ? lobbyName : defaultName;

            ConnectedLobby = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, options);

            Debug.Log("Created public Lobby: " + ConnectedLobby.Created);

            // Send a heartbeat every 15 seconds to keep the room alive
            StartCoroutine(HeartbeatLobbyCoroutine(ConnectedLobby.Id, 15));

            // Pull updates from the lobby every second
            StartCoroutine(PullUpdatesCoroutine(ConnectedLobby.Id, 1));

            Debug.Log("Started host");

            // Start the room. I'm doing this immediately, but maybe you want to wait for the lobby to fill up
            NetworkManager.Singleton.StartHost();

            // Initialize Game
            StartGame();

            return ConnectedLobby;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogFormat($"Failed creating a lobby: {e.Message}");
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

    private void StartGame()
    {
        Debug.Log("STARTING GAME");

        //disable buttons:
        _UIManager.DeactivateUI();

        _UIManager.DisplayCode(ConnectedLobby.LobbyCode);
        _UIManager.DisplayLobbyName(ConnectedLobby.Name);
        _currentMapInstance = Instantiate(_gameMap);
        SetCameraTransform(new Vector3(0f, 13.62f, 0), new Vector3(90f, 0, 0), Vector3.one);

        Debug.Log("current map: " + _currentMapInstance);
    }

    private void EndGame()
    {
        _UIManager.DisableUIText();
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
            if (ConnectedLobby != null)
            {
                if (ConnectedLobby.HostId == _playerId) Lobbies.Instance.DeleteLobbyAsync(ConnectedLobby.Id);
                else Lobbies.Instance.RemovePlayerAsync(ConnectedLobby.Id, _playerId);
            }
        }
        catch (Exception e)
        {
            Debug.Log($"Error shutting down lobby: {e}");
        }
    }
}