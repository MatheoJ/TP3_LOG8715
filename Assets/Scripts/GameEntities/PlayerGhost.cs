using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerGhost : NetworkBehaviour
{
    [SerializeField] 
    private Player m_Player;
    [SerializeField] 
    private SpriteRenderer m_SpriteRenderer;

    public override void OnNetworkSpawn()
    {
        // L'entite qui appartient au client est recoloriee en rouge
        if (IsOwner)
        {
            m_SpriteRenderer.color = Color.red;
        }
    }

    private void Update()
    {
        if (IsServer ||  !IsOwner)
        {
            transform.position = m_Player.Position;
        }
        else {
            if (m_Player.m_InputHistory.Count <= 0)
            {
                transform.position = m_Player.Position;
            }
            else
            {
                transform.position = m_Player.m_InputHistory.Last.Value.position;
            }
        }     
    }
}
