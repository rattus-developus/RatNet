using System.Collections;
using System.Collections.Generic;
using RatNet;
using Unity.VisualScripting;
using UnityEngine;

/*
    The big 2 problems here are that:
    - sometimes the latestInputTick is staying at 0
    - if latest input tick is too low, we try to access inputs out of bounds when predicting input at end of script
*/
 
public class ClientRollback : MonoBehaviour
{
    //Global singleton reference
    public static ClientRollback Instance; 

    public const int MAX_PLAYERS = 16;
    public const int RB_STATES = 32;

    public uint currentTick = 0;

    /* Buffer formatting:
        - data is in order from 0 --> (RB_STATES - 1) being currentTick --> oldestStoredTick
    */
    //Represents the game state
    public CharacterData[][] states = new CharacterData[RB_STATES][];
    //Represents inputs
    public PlayerInput[][] inputs = new PlayerInput[RB_STATES][];
    //Store the newest recieved tick from each player, so we know where to start predicting their input in the buffer
    uint[] latestInputTick = new uint[MAX_PLAYERS];

    void FixedUpdate()
    {
        //if(currentTick >= 1250) Debug.Log("AJSDNSKAJDNASKLBFLHDSAFHDSKLGSDKJHBFBLAKF");
    }

    void Awake()
    {
        Instance = this;

        for(int i = 0; i < RB_STATES; i++)
        {
            states[i] = new CharacterData[MAX_PLAYERS];  // Initialize each inner array
            inputs[i] = new PlayerInput[MAX_PLAYERS];  // Initialize each inner array
            /*
            for(int j = 0; j < MAX_PLAYERS; j++)
            {
                states[i] = new CharacterData[MAX_PLAYERS];  // Initialize each inner array
                inputs[i] = new PlayerInput[MAX_PLAYERS];  // Initialize each inner array
            }
            */
        }
        for(int i = 0; i < MAX_PLAYERS; i++)
        {
            latestInputTick[i] = 0;
        }
    }
    
    //This will be called by the client connection manager once all netcode messages have been processed into an array of inputs
    public PlayerInput Simulate(PlayerInput[] arrivedInputs)
    {
        currentTick++;

        //Increments states and inputs buffers
        CharacterData[][] newStates = new CharacterData[RB_STATES][];
        PlayerInput[][] newInputs = new PlayerInput[RB_STATES][];
        newStates[0] = new CharacterData[MAX_PLAYERS];
        newInputs[0] = new PlayerInput[MAX_PLAYERS];
        for(int i = 0; i < RB_STATES - 1; i++)
        {
            newStates[i+1] = states[i];
            newInputs[i+1] = inputs[i];
        }
        states = newStates;
        inputs = newInputs;

        uint rollbackTick = ProcessArrivedInput(arrivedInputs);
        
        
        //Add the current local player input to the inputs array here
        inputs[0][ClientConnection.Instance.localID - 1] = new PlayerInput
        {
            predicted = false,
            tick = currentTick,
            id = ClientConnection.Instance.localID,
            W = Input.GetKey(KeyCode.W),
            A = Input.GetKey(KeyCode.A),
            S = Input.GetKey(KeyCode.S),
            D = Input.GetKey(KeyCode.D),
        };
        

        //Record currnet state for later rollback
        ClientSimulation.Instance.RecordState(currentTick);

        //Rollback and simulate up to current tick here
        ClientSimulation.Instance.RollBack(rollbackTick);

        return inputs[0][ClientConnection.Instance.localID - 1];
    }
    
    //This function is assuming that we never recieve one tick without it's previous.
    //EX: If we recieved tick 5 last frame, then just tick 8 this frame, it bad (probably).
    //Returns the tick that should be rolled back to
    public uint ProcessArrivedInput(PlayerInput[] arrivedInputs)
    {  
        uint rollbackTick = currentTick;
        
        //Here we recieve an array of all recieved inputs this tick, each with a player ID and tick
        foreach(PlayerInput newInput in arrivedInputs)
        {
            if(newInput.tick > currentTick)
            {
                Debug.Log("Tick is from the future! Cannot comprehend!");
                //Debug.Log(newInput.tick - currentTick + " ticks into the future.");
                continue;
            }
            //Discard input that is too old, preventing rollback
            if(newInput.tick <= (currentTick - RB_STATES))
            {
                Debug.Log("Tick is too old! Expired!");
                //Debug.Log(currentTick - newInput.tick + " ticks in the past.");
                continue;
            }
            //Store the oldest recieved tick, so we know where to revert back to
            if(newInput.tick < rollbackTick) rollbackTick = newInput.tick;
            //Store the newest recieved tick from each player, so we know where to start predicting their input in the buffer
            if(newInput.tick > latestInputTick[newInput.id - 1]) latestInputTick[newInput.id - 1] = newInput.tick;
            
            inputs[currentTick - newInput.tick][newInput.id - 1] = newInput;
            inputs[currentTick - newInput.tick][newInput.id - 1].predicted = false;
        }

        
        //For each player, fill all buffer inputs from present back to last recieved confirmed input and predict they will continue the same inputs
        for(int i = 0; i < MAX_PLAYERS; i++)
        {
            if(!ClientConnection.Instance.activePlayers[i]) continue;

            if(i == ClientConnection.Instance.localID - 1)
            {
                continue;
            }

            if(currentTick - latestInputTick[i] < RB_STATES)
            {
                for(int j = 0; j < currentTick - latestInputTick[i]; j++)
                {
                    inputs[j][i] = inputs[currentTick - latestInputTick[i]][i];
                    inputs[j][i].predicted = true;
                }
            }
            else
            {
                for(int j = 0; j < RB_STATES; j++)
                {
                    inputs[j][i] = inputs[RB_STATES - 1][i];
                    inputs[j][i].predicted = true;
                }
            }
        }
        

        return rollbackTick;
    }
}