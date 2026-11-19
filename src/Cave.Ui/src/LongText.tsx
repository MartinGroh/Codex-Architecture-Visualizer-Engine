import { BookOpen, ChevronDown, X } from 'lucide-react'
import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type ReactNode, type SyntheticEvent } from 'react'
import { countLongTextWords, exceedsReadingBudget, getLongTextExcerpt, type LongTextFormat } from './longTextPreview'
import './LongText.css'

interface LongTextProps {
  text: string
  title: string
  byline?: string
  format?: LongTextFormat
  truncated?: boolean
  /** Use a smaller inline budget in a dense card or timeline slot. */
  compact?: boolean
  children?: ReactNode
  className?: string
  /** A new identity asks this disclosure to open once. */
  openRequest?: object
}

function stopParentInteraction(event: SyntheticEvent) { event.stopPropagation() }

/** Keeps compact views readable while revealing the caller's canonical rendering in one reader. */
export function LongText({ text, title, byline, format = 'plain', truncated = false, compact = false, children, className, openRequest }: LongTextProps) {
  const source = text.trim()
  const isLong = exceedsReadingBudget(source, format, compact)
  const excerpt = useMemo(() => isLong ? getLongTextExcerpt(source, format) : '', [source, format, isLong])
  const [{ open, consumedRequest }, setReader] = useState<{ open: boolean; consumedRequest?: object }>({ open: false })
  const opener = useRef<HTMLButtonElement>(null)
  const id = useId()

  if (isLong && openRequest && consumedRequest !== openRequest) setReader({ open: true, consumedRequest: openRequest })
  if (!isLong && open) setReader({ open: false, consumedRequest })

  const content = children ?? <div className="long-text__plain">{text}</div>
  const notice = truncated && <small className="long-text__notice">Only the captured text is available.</small>
  if (!isLong) return <>{content}{notice}</>

  return <div className={'long-text' + (compact ? ' long-text--compact' : '') + (className ? ' ' + className : '')}>
    {excerpt && <p className="long-text__excerpt">{excerpt}</p>}
    <div className="long-text__actions">
      <button ref={opener} type="button" className="long-text__open nodrag nopan nowheel"
        aria-haspopup="dialog" aria-controls={open ? id : undefined}
        onPointerDown={stopParentInteraction} onDoubleClick={stopParentInteraction} onKeyDown={stopParentInteraction}
        onClick={(event) => { event.stopPropagation(); setReader({ open: true, consumedRequest }) }}>
        <BookOpen size={15} aria-hidden="true" />{truncated ? 'Read ' : 'Read full '}{title}
      </button>
      {notice}
    </div>
    {open && <LongTextReader id={id} title={title} byline={byline} source={source} plain={format === 'plain'} notice={notice} onClose={() => {
      setReader({ open: false, consumedRequest })
      opener.current?.focus({ preventScroll: true })
    }}>{content}</LongTextReader>}
  </div>
}

interface ReaderHeading { element: HTMLElement; label: string; level: number }

function LongTextReader({ id, title, byline, source, plain, notice, children, onClose }: {
  id: string; title: string; byline?: string; source: string; plain: boolean; notice: ReactNode; children: ReactNode; onClose: () => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const viewport = useRef<HTMLDivElement>(null)
  const article = useRef<HTMLElement>(null)
  const [headings, setHeadings] = useState<ReaderHeading[]>([])
  const [contentsOpen, setContentsOpen] = useState(false)
  const [activeSection, setActiveSection] = useState(0)
  const wordCount = useMemo(() => countLongTextWords(source), [source])
  const hasOutline = headings.length > 1
  const titleId = id + '-title'
  const contentsId = id + '-contents'

  // A surrounding inspector can present its own native dialog in a layout effect.
  useEffect(() => {
    const panel = dialog.current!
    panel.showModal()
    heading.current?.focus({ preventScroll: true })
    return () => { if (panel.open) panel.close() }
  }, [])

  useLayoutEffect(() => {
    const rendered = article.current!
    const sections = [...rendered.querySelectorAll<HTMLElement>('h1, h2, h3, h4, h5, h6')]
      .filter((element) => element.closest('dialog') === dialog.current && !element.closest('[hidden], [aria-hidden="true"]'))
      .map((element) => ({ element, label: (element.textContent ?? '').replace(/\s+/gu, ' ').trim(), level: Number(element.tagName[1]) }))
      .filter(({ label }) => label.length > 0)
    setHeadings(sections)

    // Scope horizontal keyboard scrolling to the rendered blocks that actually overflow.
    const blocks = [...rendered.querySelectorAll<HTMLElement>('pre, .codex-text__table')].filter((element) => !element.hasAttribute('tabindex'))
    const setScrollStops = () => {
      for (const block of blocks) {
        if (block.scrollWidth > block.clientWidth) block.tabIndex = 0
        else block.removeAttribute('tabindex')
      }
    }
    setScrollStops()
    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(setScrollStops)
    for (const block of blocks) resize?.observe(block)
    return () => {
      resize?.disconnect()
      for (const block of blocks) block.removeAttribute('tabindex')
    }
  }, [children])

  function updateActiveSection() {
    if (!hasOutline) return
    const scrollport = viewport.current!
    if (scrollport.scrollHeight > scrollport.clientHeight
      && scrollport.scrollTop + scrollport.clientHeight >= scrollport.scrollHeight - 2) {
      setActiveSection(headings.length - 1)
      return
    }
    const threshold = scrollport.getBoundingClientRect().top + 56
    let section = 0
    for (const [index, { element }] of headings.entries()) {
      if (element.getBoundingClientRect().top > threshold) break
      section = index
    }
    setActiveSection(section)
  }

  function jumpToSection(element: HTMLElement, index: number) {
    setContentsOpen(false)
    if (!element.hasAttribute('tabindex')) element.tabIndex = -1
    element.focus({ preventScroll: true })
    const scrollport = viewport.current!
    scrollport.scrollTop += element.getBoundingClientRect().top - scrollport.getBoundingClientRect().top - 20
    setActiveSection(index)
  }

  const firstLevel = Math.min(...headings.map(({ level }) => level))
  const currentSection = Math.min(activeSection, headings.length - 1)
  return <dialog ref={dialog} id={id} className="long-text-reader nodrag nopan nowheel"
    data-outline={hasOutline || undefined} aria-labelledby={titleId} onCancel={stopParentInteraction}
    onClose={(event) => {
      if (event.target === event.currentTarget && !event.currentTarget.open) onClose()
    }} onKeyDown={stopParentInteraction} onClick={stopParentInteraction} onDoubleClick={stopParentInteraction} onPointerDown={stopParentInteraction}>
    <header className="long-text-reader__header">
      <div>
        <h2 id={titleId} ref={heading} tabIndex={-1}>{title}</h2>
        <div className="long-text-reader__meta">
          {byline && <span>{byline}</span>}
          <span>{wordCount.toLocaleString()} {wordCount === 1 ? 'word' : 'words'}</span>
        </div>
        {notice}
      </div>
      <button type="button" className="long-text-reader__close" aria-label="Close reader" onClick={() => dialog.current?.close()}>
        <X size={18} aria-hidden="true" />Close
      </button>
    </header>
    {hasOutline && <div className="long-text-reader__contents" data-expanded={contentsOpen || undefined}>
      <span className="long-text-reader__outline-label" aria-hidden="true">Contents</span>
      <button className="long-text-reader__outline-toggle" type="button" aria-expanded={contentsOpen} aria-controls={contentsId}
        onClick={() => setContentsOpen(!contentsOpen)}>Contents<ChevronDown size={16} aria-hidden="true" /></button>
      <nav id={contentsId} aria-label="Reader contents">
        <ul>
          {headings.map(({ element, label, level }, index) => <li key={index} style={{ marginInlineStart: `${Math.min(level - firstLevel, 3)}rem` }}>
            <button type="button" aria-current={index === currentSection ? 'location' : undefined}
              onClick={() => jumpToSection(element, index)}>{label}</button>
          </li>)}
        </ul>
      </nav>
    </div>}
    <div className="long-text-reader__viewport" ref={viewport} tabIndex={0} role="region" aria-label="Reading area" onScroll={updateActiveSection}>
      <article className={'long-text-reader__content' + (plain ? ' long-text__plain' : '')} ref={article}>{children}</article>
    </div>
  </dialog>
}
