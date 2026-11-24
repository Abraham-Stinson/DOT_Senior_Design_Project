using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

namespace PirateGame.Networking
{
    public struct ProbeRequest : IBroadcast { public uint Sequence; }
    public struct ProbeReply : IBroadcast
    {
        public uint Sequence;
        public uint ServerSequence;
        public int SenderClientId;
    }

    // Technical diagnostics only: clients request; only the server assigns authoritative replies.
    public sealed class NetworkProbe : MonoBehaviour
    {
        [SerializeField] private FishNetSession session;
        private readonly Dictionary<int, float> nextAllowed = new();
        private uint sequence, serverSequence, awaiting;
        private double sentAt;
        private float nextSend;
        public int Sent { get; private set; }
        public int Replies { get; private set; }
        public int ServerReceived { get; private set; }
        public int RemoteServerReceived { get; private set; }
        public string LastResult { get; private set; } = "Henüz veri doğrulanmadı.";

        private void Start()
        {
            session.Manager.ServerManager.RegisterBroadcast<ProbeRequest>(OnRequest, true);
            session.Manager.ClientManager.RegisterBroadcast<ProbeReply>(OnReply);
            session.Manager.ServerManager.OnRemoteConnectionState += OnRemoteState;
            session.Changed += OnSessionChanged;
        }

        private void OnSessionChanged()
        {
            if (session.Role == SessionRole.None)
            {
                nextAllowed.Clear();
                awaiting = sequence = serverSequence = 0;
                Sent = Replies = ServerReceived = RemoteServerReceived = 0;
                LastResult = "Henüz veri doğrulanmadı.";
            }
        }

        private void Update()
        {
            if (!session.IsAuthenticated) return;
            if (awaiting != 0 && Time.realtimeSinceStartupAsDouble - sentAt > 8)
            { LastResult = "Test mesajına 8 saniyedir yanıt yok."; awaiting = 0; }
            if (Time.unscaledTime < nextSend || awaiting != 0) return;
            SendProbe();
        }

        public void SendProbe()
        {
            if (!session.IsAuthenticated || !session.IsClient || awaiting != 0) return;
            awaiting = ++sequence;
            sentAt = Time.realtimeSinceStartupAsDouble;
            nextSend = Time.unscaledTime + 2f;
            Sent++;
            session.Manager.ClientManager.Broadcast(new ProbeRequest { Sequence = sequence }, Channel.Reliable);
        }

        private void OnRequest(NetworkConnection sender, ProbeRequest message, Channel channel)
        {
            if (!session.IsServer || !sender.IsAuthenticated || message.Sequence == 0) return;
            if (nextAllowed.TryGetValue(sender.ClientId, out float next) && Time.unscaledTime < next) return;
            nextAllowed[sender.ClientId] = Time.unscaledTime + 0.25f;
            ServerReceived++;
            if (sender.ClientId != session.Manager.ClientManager.Connection.ClientId) RemoteServerReceived++;
            session.Manager.ServerManager.Broadcast(sender, new ProbeReply
            { Sequence = message.Sequence, ServerSequence = ++serverSequence, SenderClientId = sender.ClientId }, true, Channel.Reliable);
        }

        private void OnReply(ProbeReply message, Channel channel)
        {
            if (!session.IsAuthenticated || message.Sequence != awaiting || awaiting == 0
                || message.ServerSequence == 0 || message.SenderClientId != session.Manager.ClientManager.Connection.ClientId) return;
            Replies++;
            awaiting = 0;
            double milliseconds = (Time.realtimeSinceStartupAsDouble - sentAt) * 1000;
            LastResult = $"Server ACK #{message.ServerSequence}, client #{message.SenderClientId}, RTT {milliseconds:F1} ms";
        }

        private void OnRemoteState(NetworkConnection sender, RemoteConnectionStateArgs args)
        { if (args.ConnectionState == RemoteConnectionState.Stopped) nextAllowed.Remove(args.ConnectionId); }

        private void OnDestroy()
        {
            if (session == null || session.Manager == null || !session.Manager.Initialized) return;
            session.Manager.ServerManager.UnregisterBroadcast<ProbeRequest>(OnRequest);
            session.Manager.ClientManager.UnregisterBroadcast<ProbeReply>(OnReply);
            session.Manager.ServerManager.OnRemoteConnectionState -= OnRemoteState;
            session.Changed -= OnSessionChanged;
        }
    }
}
