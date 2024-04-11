using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using static Player;

public class Player : NetworkBehaviour
{
    [SerializeField]
    private float m_Velocity;

    [SerializeField]
    private float m_Size = 1;

    private GameState m_GameState;

    

   

    public struct inputHistory
    {
        public Vector2 input;
        public Vector2 position;
        public uint timestamp;
    }

    public LinkedList<inputHistory> m_InputHistory = new LinkedList<inputHistory>();



    // GameState peut etre nul si l'entite joueur est instanciee avant de charger MainScene
    private GameState GameState
    {
        get
        {
            if (m_GameState == null)
            {
                m_GameState = FindObjectOfType<GameState>();
            }
            return m_GameState;
        }
    }

    private NetworkVariable<Vector2> m_Position = new NetworkVariable<Vector2>();
    private NetworkVariable<uint> ServerFrameNumber = new NetworkVariable<uint>(0);
    private uint ClientFrameNumber = 0;

    public Vector2 Position => m_Position.Value;

 

    private Queue<Vector2> m_InputQueue = new Queue<Vector2>();
    private Queue<uint> m_FrameCountQueue = new Queue<uint>();
    private Queue<bool> m_SpacePressedQueue = new Queue<bool>();

    private bool m_spaceHasBeeenPressed = false;

    private int frameStunnedNumber = 0;

    private void Awake()
    {
        m_GameState = FindObjectOfType<GameState>();
    }

    private void Update()
    {
        // Seuls les clients peuvent envoyer des inputs.
        if (IsClient)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
               m_spaceHasBeeenPressed = true;
            }
        }
    }


    private void FixedUpdate()
    {
        if (GameState == null )
        {
            return;
        }

        if (IsClient && IsOwner) { 
            ClientFrameNumber++;            
        }

        // Seul le serveur met à jour la position de l'entite.
        if (IsServer)
        {
            UpdatePositionServer();
        }

        // Seul le client qui possede cette entite peut envoyer ses inputs. 
        if (IsClient && IsOwner)
        {
            UpdateStunInfo();

            UpdateInputClient();

            bool needCorrection = NeedCorrection();
            if (needCorrection)
            {
                Debug.Log("Correction needed");
                CorrectPositionPlayer();                
            }

            frameStunnedNumber--;
        }
    }

    private void UpdatePositionServer()
    {
        // Mise a jour de la position selon dernier input reçu, puis consommation de l'input
        if (m_InputQueue.Count > 0)
        {
            var input = m_InputQueue.Dequeue();
            uint frame = m_FrameCountQueue.Dequeue();
            bool spacePressed = m_SpacePressedQueue.Dequeue();

            if (frame > ServerFrameNumber.Value)
            {
                ServerFrameNumber.Value = frame;
            }

            if (spacePressed)
            {
                GameState.Stun();
            }            

            if (GameState.IsStunned)
            {

                Debug.Log("Frame: " + ServerFrameNumber.Value + "position: " + m_Position.Value + "Stnned -------------------------------------------");
                return;
            }   

            m_Position.Value += input * m_Velocity * Time.fixedDeltaTime;

            // Gestion des collisions avec l'exterieur de la zone de simulation
            var size = GameState.GameSize;
            if (m_Position.Value.x - m_Size < -size.x)
            {
                m_Position.Value = new Vector2(-size.x + m_Size, m_Position.Value.y);
            }
            else if (m_Position.Value.x + m_Size > size.x)
            {
                m_Position.Value = new Vector2(size.x - m_Size, m_Position.Value.y);
            }

            if (m_Position.Value.y + m_Size > size.y)
            {
                m_Position.Value = new Vector2(m_Position.Value.x, size.y - m_Size);
            }
            else if (m_Position.Value.y - m_Size < -size.y)
            {
                m_Position.Value = new Vector2(m_Position.Value.x, -size.y + m_Size);
            }
            Debug.Log("Frame: " + ServerFrameNumber.Value + "position: " + m_Position.Value);
        }        
    }

    private void UpdateInputClient()
    {
        Vector2 inputDirection = new Vector2(0, 0);
        if (Input.GetKey(KeyCode.W))
        {
            inputDirection += Vector2.up;
        }
        if (Input.GetKey(KeyCode.A))
        {
            inputDirection += Vector2.left;
        }
        if (Input.GetKey(KeyCode.S))
        {
            inputDirection += Vector2.down;
        }
        if (Input.GetKey(KeyCode.D))
        {
            inputDirection += Vector2.right;
        }
        if (m_spaceHasBeeenPressed)
        {
            GameState.ClientStun();
        }
        if (GameState.ClientIsStunned || m_spaceHasBeeenPressed || frameStunnedNumber>0)
        {
            inputDirection = Vector2.zero;
            Debug.Log("Stunned========================= frame: " + ClientFrameNumber);
        }

        SendInputServerRpc(inputDirection.normalized);
        SendClientFrameServerRpc(ClientFrameNumber);        
        SendSpacePressedServerRpc(m_spaceHasBeeenPressed);
        m_spaceHasBeeenPressed = false;

        AddInputToHistory(inputDirection);
    }


    [ServerRpc]
    private void SendInputServerRpc(Vector2 input)
    {
        // On utilise une file pour les inputs pour les cas ou on en recoit plusieurs en meme temps.
        m_InputQueue.Enqueue(input);
    }

    [ServerRpc]
    public void SendClientFrameServerRpc(uint frameNumber)
    {
        m_FrameCountQueue.Enqueue(frameNumber);
    }

    [ServerRpc]
    public void SendSpacePressedServerRpc(bool spacePressed)
    {
        m_SpacePressedQueue.Enqueue(spacePressed);
    }


    private Vector2 SimulateMovement(Vector2 position, Vector2 input)
    {  
        Vector2 newPosition = position + input * m_Velocity * Time.fixedDeltaTime;

        var size = GameState.GameSize;
        if (newPosition.x - m_Size < -size.x)
        {
            newPosition = new Vector2(-size.x + m_Size, newPosition.y);
        }
        else if (newPosition.x + m_Size > size.x)
        {
            newPosition = new Vector2(size.x - m_Size, newPosition.y);
        }

        if (newPosition.y + m_Size > size.y)
        {
            newPosition = new Vector2(newPosition.x, size.y - m_Size);
        }
        else if (newPosition.y - m_Size < -size.y)
        {
            newPosition = new Vector2(newPosition.x, -size.y + m_Size);
        }
        return newPosition;
    }

    private bool NeedCorrection()
    {
        while (m_InputHistory.Count > 1 && m_InputHistory.First.Value.timestamp < ServerFrameNumber.Value)
        {
            m_InputHistory.RemoveFirst();
        }


        if (m_InputHistory.Count <= 1)
        {
            return false;
        }

        inputHistory inputAtThatTime = m_InputHistory.First.Value;
        Vector2 predictedPosition = inputAtThatTime.position;
        Vector2 difference = m_Position.Value - predictedPosition;

        if (difference.magnitude > -0.1)
        {
            Debug.Log("FrameOfInput: " + inputAtThatTime.timestamp + "Difference: " + difference.magnitude + "PredictedPosition: " + predictedPosition + "RealPosition: " + m_Position.Value);
        }   

        return difference.magnitude > 0.1;
    }

    private void CorrectPositionPlayer()
    {
        LinkedList<inputHistory> correctedInputHistory = new LinkedList<inputHistory>();
        int numberOfFrameStunned = 0;
        if (GameState.StunHasBegan)
        {
            numberOfFrameStunned = (int) (GameState.StunDuration / Time.fixedDeltaTime);
        }

        foreach (inputHistory input in m_InputHistory)
        {
            inputHistory newInput = new inputHistory();

            if(input.timestamp == m_InputHistory.First.Value.timestamp)
            {
                newInput.position = m_Position.Value;
            }
            else
            {
                if (numberOfFrameStunned>0)
                {
                    newInput.position = correctedInputHistory.Last.Value.position;
                }
                else
                {
                    newInput.position = SimulateMovement(correctedInputHistory.Last.Value.position, input.input);
                }
            }
            newInput.timestamp = input.timestamp;
            newInput.input = input.input;
            correctedInputHistory.AddLast(newInput);

            numberOfFrameStunned--;
            
            Debug.Log("Corrected position: " + newInput.position);
        }

        m_InputHistory = correctedInputHistory;

        if (numberOfFrameStunned > 0)
        {
            frameStunnedNumber = numberOfFrameStunned;
        }
    }

    private void AddInputToHistory(Vector2 inputDirection)
    {
        inputHistory newInput = new inputHistory();
        newInput.input = inputDirection.normalized;
        if (m_InputHistory.Count > 0)
        {
            if (!GameState.ClientIsStunned)
            {
                newInput.position = SimulateMovement(m_InputHistory.Last.Value.position, inputDirection.normalized);
            }
            else
            {
                newInput.position = m_InputHistory.Last.Value.position;
            }
        }
        else
        {
            newInput.position = m_Position.Value;
        }
        newInput.timestamp = ClientFrameNumber;
        m_InputHistory.AddLast(newInput);


    }

    public void UpdateStunInfo()
    {
        if (GameState.IsStunned != GameState.LastUpdateIsStunned && GameState.IsStunned)
        {
            GameState.StunHasBegan = true;
        }
        else
        {
            GameState.StunHasBegan = false;
        }
        GameState.LastUpdateIsStunned = GameState.IsStunned;        
    }
}
