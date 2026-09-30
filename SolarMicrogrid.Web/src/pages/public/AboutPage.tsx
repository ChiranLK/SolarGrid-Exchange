import { useEffect, useRef } from 'react'
import { Link } from 'react-router-dom'
import { ArrowRightIcon, EyeIcon, LeafIcon, MapPinIcon, ScaleIcon, ShieldIcon } from '../../components/icons'
import { useReveal } from './useReveal'

const values = [
  {
    icon: LeafIcon,
    title: 'Local first',
    body: 'Energy made in a neighbourhood should serve that neighbourhood. Short distances waste less and mean more to the people nearby.',
  },
  {
    icon: ScaleIcon,
    title: 'Fair access',
    body: 'Every member books from the same open slots, with the same rules. No one can quietly jump the queue.',
  },
  {
    icon: EyeIcon,
    title: 'Nothing hidden',
    body: 'You can see how much capacity is left, where your booking stands and every change made to it.',
  },
  {
    icon: ShieldIcon,
    title: 'Safe handovers',
    body: 'Energy is only released after a trained operator checks your pass on site. It protects you and the station.',
  },
]

const milestones = [
  { label: 'The idea', body: 'Rooftop and community solar was growing fast, but there was no simple way for neighbours to share what it produced.' },
  { label: 'The pilot', body: 'We started with a handful of stations and a small group of households to learn what people actually needed.' },
  { label: 'Today', body: 'Members book through the app, operators run each station, and our Backoffice team looks after the whole network.' },
]

const nodes = [
  { x: 70, y: 90, kind: 'home' },
  { x: 60, y: 250, kind: 'home' },
  { x: 180, y: 170, kind: 'station' },
  { x: 300, y: 70, kind: 'home' },
  { x: 320, y: 280, kind: 'home' },
  { x: 430, y: 170, kind: 'station' },
  { x: 520, y: 80, kind: 'home' },
  { x: 530, y: 270, kind: 'home' },
] as const

const links: Array<[number, number]> = [[0, 2], [1, 2], [2, 3], [2, 4], [2, 5], [3, 5], [4, 5], [5, 6], [5, 7]]

function NetworkScene() {
  return (
    <svg className="network-scene" viewBox="0 0 600 350" aria-hidden="true" focusable="false">
      {links.map(([from, to], index) => {
        const a = nodes[from]
        const b = nodes[to]
        const d = `M${a.x} ${a.y} L${b.x} ${b.y}`
        return (
          <g key={`${from}-${to}`}>
            <path className="ns-link" d={d} />
            <path className="ns-pulse" d={d} style={{ animationDelay: `${index * 0.45}s` }} />
          </g>
        )
      })}
      {nodes.map((node, index) => (
        <g key={index} className={`ns-node ns-${node.kind}`} style={{ animationDelay: `${index * 0.3}s` }}>
          <circle className="ns-halo" cx={node.x} cy={node.y} r={node.kind === 'station' ? 34 : 22} />
          <circle className="ns-core" cx={node.x} cy={node.y} r={node.kind === 'station' ? 20 : 12} />
          {node.kind === 'station' ? (
            <path className="ns-glyph" d={`M${node.x + 2} ${node.y - 10} ${node.x - 7} ${node.y + 2}h7l-2 9 9-12h-7l2-9z`} />
          ) : (
            <path className="ns-glyph" d={`M${node.x - 6} ${node.y + 5}v-6l6-5 6 5v6z`} />
          )}
        </g>
      ))}
    </svg>
  )
}

export function AboutPage() {
  const pageRef = useRef<HTMLDivElement>(null)
  useReveal(pageRef)

  useEffect(() => {
    document.title = 'About us | SolarGrid Exchange'
  }, [])

  return (
    <div ref={pageRef}>
      <section className="page-intro">
        <div className="site-container">
          <p className="site-kicker"><span className="site-kicker-dot" /> About SolarGrid Exchange</p>
          <h1 className="page-intro-title">We help neighbours share the sun.</h1>
          <p className="page-intro-lead">
            Sri Lanka has sunshine for most of the year. SolarGrid Exchange makes it easy for a
            community to use the solar power it produces, fairly and close to home.
          </p>
        </div>
      </section>

      <section className="site-section pt-0">
        <div className="site-container">
          <div className="about-story glass-card" data-reveal>
            <div className="about-story-copy">
              <p className="site-eyebrow">Our story</p>
              <h2 className="site-section-title">A small exchange for every neighbourhood.</h2>
              <p>
                Solar stations produce the most energy in the middle of the day, when many homes
                need it least. We built SolarGrid Exchange so that energy has somewhere useful to go.
              </p>
              <p>
                Members reserve what they need from a station nearby. Operators on site make sure
                each handover is correct. Our Backoffice team keeps the network running, approves
                new members and looks after every station and time slot.
              </p>
              <p className="about-location">
                <MapPinIcon width={18} height={18} /> Based in Colombo, working with communities across Sri Lanka.
              </p>
            </div>
            <div className="about-story-visual">
              <NetworkScene />
              <div className="network-legend">
                <span><i className="is-station" /> Solar station</span>
                <span><i className="is-home" /> Member home</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="site-section site-section-tint" aria-labelledby="values-title">
        <div className="site-container">
          <div className="site-section-head" data-reveal>
            <p className="site-eyebrow">What we stand for</p>
            <h2 id="values-title" className="site-section-title">Four promises we build around.</h2>
          </div>
          <div className="value-grid">
            {values.map((value, index) => (
              <article
                key={value.title}
                className="glass-card feature-card"
                data-reveal
                style={{ transitionDelay: `${index * 70}ms` }}
              >
                <span className="feature-icon"><value.icon width={22} height={22} /></span>
                <h3>{value.title}</h3>
                <p>{value.body}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      <section className="site-section" aria-labelledby="journey-title">
        <div className="site-container">
          <div className="site-section-head" data-reveal>
            <p className="site-eyebrow">How we got here</p>
            <h2 id="journey-title" className="site-section-title">Started small, on purpose.</h2>
          </div>
          <ol className="timeline">
            {milestones.map((milestone, index) => (
              <li key={milestone.label} data-reveal style={{ transitionDelay: `${index * 90}ms` }}>
                <span className="timeline-dot" />
                <h3>{milestone.label}</h3>
                <p>{milestone.body}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      <section className="site-section site-section-last">
        <div className="site-container">
          <div className="cta-band" data-reveal>
            <div>
              <h2>Want a station in your area?</h2>
              <p>Tell us where you are. We are always looking for new communities to work with.</p>
            </div>
            <div className="cta-band-actions">
              <Link to="/contact" className="btn site-btn site-btn-light site-btn-lg">
                Contact us <ArrowRightIcon width={18} height={18} />
              </Link>
            </div>
          </div>
        </div>
      </section>
    </div>
  )
}
