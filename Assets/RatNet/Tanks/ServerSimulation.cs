using System.Collections;
using System.Collections.Generic;
using RatNet;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

public class ServerSimulation : MonoBehaviour
{
    //Global singleton reference
    public static ServerSimulation Instance;

    //An array containing all player objects. The size of the array is the number of current players.
    //When a new player joins, a new tank should be added
    [SerializeField] GameObject tankPrefab;
    public Transform[] tanks = new Transform[ClientRollback.MAX_PLAYERS];
    [SerializeField] float speed = 0.0625f;
    //Represents inputs
    //Here the current state will attempt to be as rece
    public PlayerInput[][] storedInputs = new PlayerInput[FUTURE_INPUTS][];
    public int[] wPressed = new int[FUTURE_INPUTS];

    public const int FUTURE_INPUTS = 32;
    public const int MAX_SIM_WAIT_TICKS = 24;
    //public const int STATE_SYNC_INTERVAL = 5;
    public uint tickAwaitingInputs = 1;

    void Awake()
    {
        Instance = this;

        for(int i = 0; i < ClientRollback.RB_STATES; i++)
        {
            storedInputs[i] = new PlayerInput[ClientRollback.MAX_PLAYERS];  // Initialize each inner array
            for(int j = 0; j < ClientRollback.MAX_PLAYERS; j++)
            {
                storedInputs[i][j] = new PlayerInput{predicted = true};  // Initialize each inner array
            }
        }
    }

    /*
    1. Add newly arrived inputs to 2D array if they're valid
    2. If we have all the inputs (or have been waiting for more then MAX_SIMWAIT), simulate next tick
    3. while we have all the next inputs, continue to simulate until we cant anymore
    (DONT FROGET to increment data every time, setting predicted to truye on the new blank data)
    4. return 0 if no state update, return the tick to update otherwise (if we just simulated it and its divisible by STATE_SYNC_INTERVAL)
    (this will tell ServerConnection whether to send a game state update or not)
    */
    public uint ProcessArrivedInput(PlayerInput[] arrivedInputs)
    {
        for(int i = 0; i < ServerConnection.Instance.playerCount; i++)
        {
            if(ServerConnection.Instance.m_Connections[i] != default && tanks[i] == null)
            {
                //Add player
                tanks[i] = Instantiate(tankPrefab).transform;
                Debug.Log("creating");
                //tanks[i].name = "Tank " + (i + 1);
            }
            else if(ServerConnection.Instance.m_Connections[i] == default && tanks[i] != null)
            {
                Debug.Log("destroying");
                Destroy(tanks[i].gameObject);
            }
        }

        uint toReturn = 0;

        //Check if arrived inputs fit into stored input buffer, then add them if so
        foreach(PlayerInput newInput in arrivedInputs)
        {
            if(newInput.tick > tickAwaitingInputs + FUTURE_INPUTS)
            {
                Debug.Log("Tick is from the future! Cannot comprehend!");
                continue;
            }
            //Discard input that is too old, preventing rollback
            if(newInput.tick < tickAwaitingInputs)
            {
                Debug.Log("Tick is too old! Expired!");
                continue;
            }
            
            Debug.Log("1: " + (FUTURE_INPUTS - 1));
            Debug.Log("2: " + (newInput.tick - tickAwaitingInputs));
            Debug.Log("3: " + ((FUTURE_INPUTS - 1) - (newInput.tick - tickAwaitingInputs)));
            
            storedInputs[(FUTURE_INPUTS - 1) - (newInput.tick - tickAwaitingInputs)][newInput.id - 1] = newInput;
            storedInputs[(FUTURE_INPUTS - 1) - (newInput.tick - tickAwaitingInputs)][newInput.id - 1].predicted = false;
        }

        bool canSim = true;
        foreach(PlayerInput inp in storedInputs[FUTURE_INPUTS - 1])
        {
            if(inp.predicted) canSim = false;
        }

        //If all inputs for tick have been recieved, or we have been waiting too long to simulate, simulate
        if(canSim || ServerConnection.Instance.realtimeTick - tickAwaitingInputs > MAX_SIM_WAIT_TICKS)
        {
            if(ServerConnection.Instance.realtimeTick - tickAwaitingInputs > MAX_SIM_WAIT_TICKS) Debug.Log("Sme inputs not recieved in time. Advancing without them.");


            toReturn = tickAwaitingInputs;
            Simulate(storedInputs[FUTURE_INPUTS - 1]);

            //Increments states and inputs buffers
            PlayerInput[][] newInputs = new PlayerInput[FUTURE_INPUTS][];
            newInputs[0] = new PlayerInput[ClientRollback.MAX_PLAYERS];
            for(int i = 0; i < ClientRollback.MAX_PLAYERS; i++)
            {
                newInputs[0][i].predicted = true;
            }
            for(int i = 0; i < FUTURE_INPUTS - 1; i++)
            {
                newInputs[i+1] = storedInputs[i];
            }
            storedInputs = newInputs;
            tickAwaitingInputs++;
        }

        for(int i = 0; i < FUTURE_INPUTS; i++)
        {
            wPressed[i] = storedInputs[i][0].W? 1: 0;
        }

        return toReturn;
    }

    //This is the function where all the real simulation happens. All it does is perform one "step" of simulation on the current game state given the inputs
    //After the last one is called on this frame, we update the visuals according to the most recent player data
    void Simulate(PlayerInput[] inputs)
    {
        for(int i = 0; i < ServerConnection.Instance.m_Connections.Length; i++)
        {
            if(ServerConnection.Instance.m_Connections[i] == default) continue;

            //Handle inputs
            Vector2 moveDir = Vector2.zero;
            if(inputs[i].W)
            {
                moveDir.y += 1;
                //Debug.Log("forward");
            }
            if(inputs[i].S)
            {
                moveDir.y -= 1;
            }
            if(inputs[i].D)
            {
                moveDir.x += 1;
            }
            if(inputs[i].A)
            {
                moveDir.x -= 1;
            }

            moveDir.Normalize();
            moveDir *= speed;

            tanks[i].position = new Vector3(tanks[i].position.x + moveDir.x, tanks[i].position.y, tanks[i].position.z + moveDir.y);

            /*
            if(i == 0)
            {
                Debug.Log(moveDir);
                Debug.Log(tanks[i].position);
            }
            */
        }
    }

    public void RecordState(uint recordTick)
    {
        //Record current game in the given tick
        for(int i = 0; i < tanks.Length; i++)
        {
            if(!ClientConnection.Instance.activePlayers[i]) continue;
            if(tanks[i] == null) continue;
            //Debug.Log(ClientRollback.Instance.states[ClientRollback.Instance.currentTick - recordTick][i + 1]);
            ClientRollback.Instance.states[ClientRollback.Instance.currentTick - recordTick][i + 1].position = tanks[i].position;
            ClientRollback.Instance.states[ClientRollback.Instance.currentTick - recordTick][i + 1].rotation = tanks[i].rotation;
        }
    }
}
