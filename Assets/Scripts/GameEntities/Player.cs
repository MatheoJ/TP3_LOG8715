using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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
        public float timestamp;
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

    public Vector2 Position => m_Position.Value;

    private Queue<Vector2> m_InputQueue = new Queue<Vector2>();

    private void Awake()
    {
        m_GameState = FindObjectOfType<GameState>();
    }

    private void FixedUpdate()
    {
        // Si le stun est active, rien n'est mis a jour.
        if (GameState == null )
        {
            return;
        }

        if (GameState.IsStunned)
        {
            inputHistory newInput = new inputHistory();
            newInput.input = Vector2.zero;
            if (m_InputHistory.Count > 0)
            {
                newInput.position = m_InputHistory.Last.Value.position;
            }
            else
            {
                newInput.position = m_Position.Value;
            }
            newInput.timestamp = Time.time;
            return;
        }

        // Seul le serveur met à jour la position de l'entite.
        if (IsServer)
        {
            UpdatePositionServer();
        }

        // Seul le client qui possede cette entite peut envoyer ses inputs. 
        if (IsClient && IsOwner)
        {
            UpdateInputClient();
            //RemoveOldInputs();

            bool needCorrection = NeedCorrection();
            if (needCorrection)
            {
                Debug.Log("Correction needed");

                CorrectPositionPlayer();                
            }
        }
    }

    private void UpdatePositionServer()
    {
        // Mise a jour de la position selon dernier input reçu, puis consommation de l'input
        if (m_InputQueue.Count > 0)
        {
            var input = m_InputQueue.Dequeue();
            m_Position.Value += input * m_Velocity * Time.deltaTime;

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
        SendInputServerRpc(inputDirection.normalized);


        
        inputHistory newInput = new inputHistory();
        newInput.input = inputDirection.normalized;
        if (m_InputHistory.Count > 0)
        {
            newInput.position = SimulateMovement(m_InputHistory.Last.Value.position, inputDirection.normalized);
        }
        else
        {
            newInput.position = m_Position.Value;
        }
        newInput.timestamp = Time.time;
        m_InputHistory.AddLast(newInput);
    }


    [ServerRpc]
    private void SendInputServerRpc(Vector2 input)
    {
        // On utilise une file pour les inputs pour les cas ou on en recoit plusieurs en meme temps.
        m_InputQueue.Enqueue(input);
    }

    private Vector2 SimulateMovement(Vector2 position, Vector2 input)
    {
        Vector2 newPosition = position + input * m_Velocity * Time.deltaTime;

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

    private void RemoveOldInputs()
    {
        float currentRTT = GameState.CurrentRTT;

        while (m_InputHistory.Count > 0 && m_InputHistory.First.Value.timestamp < Time.time - currentRTT - Time.deltaTime)
        {
            m_InputHistory.RemoveFirst();
        }
    }

    private bool NeedCorrection()
    {

        if (m_InputHistory.Count <= 1)
        {
            return false;
        }

        float timeOfInputSent = Time.time - GameState.CurrentRTT - Time.deltaTime;
        float diffWithHead = Mathf.Abs(m_InputHistory.First.Value.timestamp - timeOfInputSent);
        float diffWithHeadPlusOne = Mathf.Abs(m_InputHistory.First.Next.Value.timestamp - timeOfInputSent);

        while (m_InputHistory.Count > 1 && diffWithHeadPlusOne < diffWithHead)
        {
            m_InputHistory.RemoveFirst();
            diffWithHead = Mathf.Abs(m_InputHistory.First.Value.timestamp - timeOfInputSent);
            diffWithHeadPlusOne = Mathf.Abs(m_InputHistory.First.Next.Value.timestamp - timeOfInputSent);
        }

        if (m_InputHistory.Count <= 1)
        {
            return false;
        }

        inputHistory inputAtThatTime = m_InputHistory.First.Value;
        Vector2 predictedPosition = inputAtThatTime.position;
        Vector2 difference = m_Position.Value - predictedPosition;
        if (difference.magnitude > -1.0)
        {
            Debug.Log("TimeOfInput: " + inputAtThatTime.timestamp);
            Debug.Log("TimePredicted: " + (Time.time - GameState.CurrentRTT - Time.deltaTime));
            Debug.Log("Difference: " + difference.magnitude);
        }


        return difference.magnitude > 1.0;
    }

    private void CorrectPositionPlayer()
    {
        LinkedList<inputHistory> correctedInputHistory = new LinkedList<inputHistory>();
        foreach (inputHistory input in m_InputHistory)
        {
            inputHistory newInput = new inputHistory();

            if(input.timestamp == m_InputHistory.First.Value.timestamp)
            {
                newInput.position = m_Position.Value;
            }
            else
            {
                newInput.position = SimulateMovement(correctedInputHistory.Last.Value.position, input.input);
            }
            newInput.timestamp = input.timestamp;
            newInput.input = input.input;
            correctedInputHistory.AddLast(newInput);
        }

        m_InputHistory = correctedInputHistory;
    }
    



}
