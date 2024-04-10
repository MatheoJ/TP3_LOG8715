using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CircleGhost : NetworkBehaviour
{
    [SerializeField]
    private MovingCircle m_MovingCircle;

    private GameState m_GameState;

    private bool GameWasStunnedLastFrame = false;
    private Vector2 storedVelocity = Vector2.zero;

    private float timeLeftToSimulate = 0.0f;


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
    private void Awake()
    {
        m_GameState = FindObjectOfType<GameState>();
    }
    private void Update()
    {
        if (m_GameState.ClientIsStunned)
        {
            if (!GameWasStunnedLastFrame)
            {
                storedVelocity = m_MovingCircle.Velocity;
            }
            GameWasStunnedLastFrame = true;
            return;
        }
        else if (GameWasStunnedLastFrame)
        {
            m_MovingCircle.simulatedPosition = transform.position;
            m_MovingCircle.simulatedVelocity = storedVelocity;
            timeLeftToSimulate = m_GameState.CurrentRTT * 2;
            GameWasStunnedLastFrame = false;
            return;
        }

        if(timeLeftToSimulate > 0)
        {
            timeLeftToSimulate -= Time.deltaTime;
            transform.position = m_MovingCircle.simulatedPosition;
            return;
        }

        transform.position = m_MovingCircle.Position;
    }
}
