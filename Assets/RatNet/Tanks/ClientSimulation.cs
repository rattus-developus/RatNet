using System.Collections;
using System.Collections.Generic;
using RatNet;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

/* Rollback Behaviours:
- MUST loop through each player and act on them
- MUST work at a fixed step / framerate

- CAN access the input struct for each player
- CAN access the data struct for each player (as well as a "global" data struct for things such as projectiles and other networked non-player objects)
- MUST act on one of these otherwise it doesn't need to be rollback

- SHOULD use some form of input delay to make rollbacks less often?

*/

public struct CharacterData
{
    public Vector3 position;
    public Quaternion rotation;
    public uint tick;
    public byte id;
}

public struct PlayerInput
{
    public bool W;
    public bool A;
    public bool S;
    public bool D;
    public uint tick;
    public byte id;
    //This will help differentiate between input structs with predicted data and ones with definitive recieved data
    public bool predicted;
}

public class ClientSimulation : MonoBehaviour
{
    //Global singleton reference
    public static ClientSimulation Instance;

    //An array containing all player objects. The size of the array is the number of current players.
    //When a new player joins, a new tank should be added
    [SerializeField] Transform[] tanks;
    [SerializeField] float speed;

    void Awake()
    {
        Instance = this;
    }

    //This is the function where all the real simulation happens. All it does is perform one "step" of simulation on the current game state given the inputs
    //After the last one is called on this frame, we update the visuals according to the most recent player data
    void Simulate(PlayerInput[] inputs)
    {
        for(int i = 0; i < tanks.Length; i++)
        {
            //Handle inputs
            Vector2 moveDir = Vector2.zero;
            if(inputs[i].W)
            {
                moveDir.y += 1;
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
                moveDir.x += 1;
            }

            moveDir.Normalize();
            moveDir *= speed;

            tanks[i].position = new Vector3(tanks[i].position.x + moveDir.x, tanks[i].position.y, tanks[i].position.z + moveDir.y);

            //Debug.Log("Here");
            //Debug.Log(moveDir);
        }
    }

    public void RecordState(uint recordTick)
    {
        //Record current game in the given tick
        for(int i = 0; i < tanks.Length; i++)
        {
            ClientRollback.Instance.states[ClientRollback.Instance.currentTick - recordTick][i + 1].position = tanks[i].position;
            ClientRollback.Instance.states[ClientRollback.Instance.currentTick - recordTick][i + 1].rotation = tanks[i].rotation;
        }
    }

    void SetState(uint rollbackTick)
    {
        //Set game state back to how it was recorded at a given tick
        //Currently this is just the tanks positions and rotations
        for(int i = 0; i < tanks.Length; i++)
        {
            tanks[i].position = ClientRollback.Instance.states[ClientRollback.Instance.currentTick - rollbackTick][i + 1].position;
            tanks[i].rotation = ClientRollback.Instance.states[ClientRollback.Instance.currentTick - rollbackTick][i + 1].rotation;
        }
    }

    //This is called every frame with the given tick as the farthest to rollback to
    public void RollBack(uint rollbackTick)
    {
        if(rollbackTick == ClientRollback.Instance.currentTick)
        {
            Simulate(ClientRollback.Instance.inputs[0]);
            return;
        }

        SetState(rollbackTick);

        //Simulate up through current tick, recording game data at each tick along the way
        for(uint i = rollbackTick; i <= ClientRollback.Instance.currentTick; i++)
        {
            //For each player:
            //Respond to the given tick's input
            //Save game state to the tick just simulated + 1
            //the actual "current state" isnt stored yet, it jsut is. the most recent saved state is what was seen last frame usually
            //Saved game state shoudl always be "what occured as a result of the input from the tick before"

            Simulate(ClientRollback.Instance.inputs[i]);

            //Set state for i + 1, newly updated for the rolled back changes
            if(i < ClientRollback.Instance.currentTick)
            RecordState(i+1);
        }
    }
}
