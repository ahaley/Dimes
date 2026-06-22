import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'

/** Subscribe to realtime board updates for a project. On any change broadcast (create / edit /
 * transition / promote) it invalidates the project's board + inbox queries so every viewer refreshes.
 * Connection failures are non-fatal — the app still works, just without live updates. */
export function useBoardLiveUpdates(projectId: string | undefined) {
  const qc = useQueryClient()

  useEffect(() => {
    if (!projectId) return

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/board')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    const refresh = () => {
      qc.invalidateQueries({ queryKey: ['changes', projectId] })
      qc.invalidateQueries({ queryKey: ['inbox', projectId] })
      // Capture Assist conversations + the assistant's pending list + the requester's resume list
      // (coarse but cheap + idempotent).
      qc.invalidateQueries({ queryKey: ['assist'] })
      qc.invalidateQueries({ queryKey: ['assist-pending', projectId] })
      qc.invalidateQueries({ queryKey: ['assist-mine', projectId] })
    }
    connection.on('boardChanged', refresh)
    // Re-join the project group after an automatic reconnect.
    connection.onreconnected(() => { connection.invoke('JoinProject', projectId).catch(() => {}) })

    let cancelled = false
    connection.start()
      .then(() => { if (!cancelled) return connection.invoke('JoinProject', projectId) })
      .catch(() => { /* offline / not connected — queries still refetch normally */ })

    return () => {
      cancelled = true
      connection.off('boardChanged', refresh)
      if (connection.state === HubConnectionState.Connected) {
        connection.invoke('LeaveProject', projectId).catch(() => {})
      }
      void connection.stop()
    }
  }, [projectId, qc])
}
