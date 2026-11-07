import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import typesSource from './types.ts?raw'
import { api } from './client'

function makeResponse(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response
}

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('M6c 契约回归 — GET /api/pipeline 链路泳道（先锁契约再实现）', () => {
  it('types.ts 锁死 PipelineNode / PipelineSnapshot 与 8 个节点字段', () => {
    for (const token of [
      'export interface PipelineNode',
      'export interface PipelineSnapshot',
      'key: string', 'label: string', 'middleware: string', 'count: number',
      'unit: string', 'source: string', 'available: boolean', 'link: string',
      'nodes: PipelineNode[]',
    ]) {
      expect(typesSource).toContain(token)
    }
  })

  it('api.getPipeline → GET /api/pipeline', async () => {
    expect(typeof (api as unknown as Record<string, unknown>).getPipeline).toBe('function')
    fetchMock.mockResolvedValue(makeResponse({ generatedAtUtc: '2026-11-07T00:00:00Z', nodes: [] }))
    await (api as unknown as { getPipeline: () => Promise<unknown> }).getPipeline()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/pipeline')
  })
})
