import type { CodexGoalSnapshot, ConversationOverlay } from './types'

/** Do not reuse a private goal or a goal from a different hooked task. */
export function sharedGoal(conversation: ConversationOverlay): CodexGoalSnapshot | null {
  const goal = conversation.goal
  return conversation.sharingEnabled && goal?.sessionId === conversation.control.sessionId
    ? goal ?? null : null
}
