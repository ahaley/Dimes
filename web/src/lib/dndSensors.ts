import { KeyboardSensor, MouseSensor, TouchSensor, useSensor, useSensors } from '@dnd-kit/core'
import { sortableKeyboardCoordinates } from '@dnd-kit/sortable'

/**
 * Shared drag-and-drop sensors for the board and the sidebar, tuned so dragging coexists with touch
 * scrolling instead of fighting it:
 *
 * - MouseSensor — desktop drag starts after a 5px move, so a plain click still selects/navigates.
 * - TouchSensor — a 250ms long-press starts a drag; a normal (quick) swipe scrolls the list instead
 *   of grabbing a card. This is why we use the Mouse+Touch pair rather than a single PointerSensor:
 *   PointerSensor would apply the same 5px threshold to touch and hijack vertical scroll gestures.
 * - KeyboardSensor — arrow-key dragging for keyboard/accessibility users.
 */
export function useBoardSensors() {
  return useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 5 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 8 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )
}
