import { describe, expect, it } from 'vitest'
import { agentDisplayName } from './agentIdentity'

describe('agentDisplayName', () => {
  it('keeps an alias tied to identity rather than phase, role, or array order', () => {
    const agent = { agentId: 'child-review', isSubagent: true }
    expect(agentDisplayName(agent)).toMatch(/^[A-Z][a-z]+ [A-Z][a-z]+ · [a-z0-9]+$/)
    expect(agentDisplayName({ ...agent })).toBe(agentDisplayName(agent))
    expect(['child-docs', 'child-review'].map((agentId) => agentDisplayName({ agentId, isSubagent: true })).reverse())
      .toEqual(['child-review', 'child-docs'].map((agentId) => agentDisplayName({ agentId, isSubagent: true })))
    expect(agentDisplayName({ agentId: 'child-docs', isSubagent: true })).not.toBe(agentDisplayName(agent))
  })
  it('preserves the main agent role', () => {
    expect(agentDisplayName({ agentId: 'main-task', isSubagent: false })).toBe('Main agent')
  })
  it('disambiguates agent IDs that share the same generated name pair', () => {
    const names = ['review-child-138', 'review-child-220'].map((agentId) => agentDisplayName({ agentId, isSubagent: true }))
    expect(names[0].split(' · ')[0]).toBe(names[1].split(' · ')[0])
    expect(names[0]).not.toBe(names[1])
  })
})
