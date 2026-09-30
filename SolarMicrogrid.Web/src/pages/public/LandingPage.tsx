import { useEffect, useRef } from 'react'
import { Link } from 'react-router-dom'
import {
  ArrowRightIcon,
  BoltIcon,
  CalendarIcon,
  CheckIcon,
  HistoryIcon,
  MapPinIcon,
  PhoneIcon,
  QrIcon,
  ShieldIcon,
  StationIcon,
  UsersIcon,
} from '../../components/icons'
import { HeroScene } from './HeroScene'
import { PhoneMockup } from './PhoneMockup'
import { useReveal } from './useReveal'

const quickFacts = [
  { value: '25 km', label: 'Stations shown around you' },
  { value: '7 days', label: 'How far ahead you can book' },
  { value: '12 hrs', label: 'Notice to change or cancel' },
]

const features = [
  {
    icon: MapPinIcon,
    title: 'Find a station nearby',
    body: 'See solar stations around you on a map or as a list, with the capacity left in each time slot.',
  },
  {
    icon: CalendarIcon,
    title: 'Reserve the energy you need',
    body: 'Pick a slot, enter how many kWh you want and send your request. Book up to a week ahead.',
  },
  {
    icon: QrIcon,
    title: 'A QR pass for every booking',
    body: 'Once your booking is approved, the app shows a short-lived QR code for you to present at the station.',
  },
  {
    icon: ShieldIcon,
    title: 'Checked at the station',
    body: 'A Grid Operator scans your code and confirms the handover, so the energy goes to the right person.',
  },
  {
    icon: HistoryIcon,
    title: 'Plans change, that is fine',
    body: 'Move or cancel a booking up to 12 hours before it starts. Every change is kept in your history.',
  },
  {
    icon: UsersIcon,
    title: 'Real people, verified',
    body: 'New accounts are reviewed by our Backoffice team before they can book, which keeps the community fair.',
  },
]

const steps = [
  { title: 'Create your account', body: 'Download the SolarGrid app and register with your NIC, phone number and email.' },
  { title: 'Get approved', body: 'Our team checks your details and activates your account before your first booking.' },
  { title: 'Book a slot', body: 'Choose a station, a time and the amount of energy you need. Follow its status right in the app.' },
  { title: 'Collect your energy', body: 'Show your QR pass at the station. The operator confirms it and your transfer is complete.' },
]

const roles = [
  {
    icon: PhoneIcon,
    name: 'Prosumers',
    where: 'Android app',
    body: 'Households and small businesses who book and collect solar energy.',
    points: ['Browse nearby stations', 'Book, change or cancel', 'Show a QR pass at pickup'],
  },
  {
    icon: BoltIcon,
    name: 'Grid Operators',
    where: 'Web and Android',
    body: 'Station staff who keep each site running and hand over energy safely.',
    points: ['Live view of their station', 'Scan and verify QR passes', 'Confirm completed transfers'],
  },
  {
    icon: StationIcon,
    name: 'Backoffice',
    where: 'Web portal',
    body: 'The team that looks after members, stations and bookings across the network.',
    points: ['Approve new members', 'Manage stations and slots', 'Review every reservation'],
  },
]

const appPoints = [
  'Stations on a map, sorted by distance',
  'Live slot capacity before you book',
  'Your QR pass, ready when you arrive',
  'Light and dark themes',
]

export function LandingPage() {
  const pageRef = useRef<HTMLDivElement>(null)
  useReveal(pageRef)

  useEffect(() => {
    document.title = 'SolarGrid Exchange | Clean solar energy, shared locally'
  }, [])

  return (
    <div ref={pageRef} className="landing">
      {/* Hero */}
      <section className="site-hero">
        <div className="site-container site-hero-grid">
          <div className="site-hero-copy">
            <p className="site-kicker"><span className="site-kicker-dot" /> Community solar for Sri Lanka</p>
            <h1 className="site-hero-title">
              Clean solar energy, <em>booked</em> from a station near you.
            </h1>
            <p className="site-hero-lead">
              SolarGrid Exchange connects homes with local solar stations. Reserve an energy slot on
              your phone, arrive with your QR pass and collect power that was made in your own
              neighbourhood.
            </p>
            <div className="site-hero-actions">
              <Link to="/#mobile-app" className="btn site-btn site-btn-primary site-btn-lg">
                Get the app <ArrowRightIcon width={18} height={18} />
              </Link>
              <Link to="/login" className="btn site-btn site-btn-ghost site-btn-lg">Sign in</Link>
            </div>
            <dl className="site-facts">
              {quickFacts.map((fact) => (
                <div key={fact.label}>
                  <dt>{fact.label}</dt>
                  <dd>{fact.value}</dd>
                </div>
              ))}
            </dl>
          </div>
          <div className="site-hero-visual">
            <HeroScene />
          </div>
        </div>
      </section>

      {/* Features */}
      <section id="features" className="site-section" aria-labelledby="features-title">
        <div className="site-container">
          <div className="site-section-head" data-reveal>
            <p className="site-eyebrow">What you can do</p>
            <h2 id="features-title" className="site-section-title">Everything you need, from booking to pickup.</h2>
            <p className="site-section-lead">
              We kept it simple. Find a station, reserve a slot and collect your energy. The app
              looks after the rest and tells you where things stand.
            </p>
          </div>
          <div className="feature-grid">
            {features.map((feature, index) => (
              <article
                key={feature.title}
                className="glass-card feature-card"
                data-reveal
                style={{ transitionDelay: `${(index % 3) * 70}ms` }}
              >
                <span className="feature-icon"><feature.icon width={22} height={22} /></span>
                <h3>{feature.title}</h3>
                <p>{feature.body}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      {/* How it works */}
      <section id="how-it-works" className="site-section site-section-tint" aria-labelledby="how-title">
        <div className="site-container">
          <div className="site-section-head" data-reveal>
            <p className="site-eyebrow">How it works</p>
            <h2 id="how-title" className="site-section-title">Four steps from sign&#8209;up to switch&#8209;on.</h2>
          </div>
          <ol className="steps">
            {steps.map((step, index) => (
              <li key={step.title} className="step" data-reveal style={{ transitionDelay: `${index * 90}ms` }}>
                <span className="step-number">{String(index + 1).padStart(2, '0')}</span>
                <h3>{step.title}</h3>
                <p>{step.body}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* Roles */}
      <section className="site-section" aria-labelledby="roles-title">
        <div className="site-container">
          <div className="site-section-head" data-reveal>
            <p className="site-eyebrow">Who it is for</p>
            <h2 id="roles-title" className="site-section-title">One exchange, three kinds of members.</h2>
            <p className="site-section-lead">
              Everyone sees what they need and nothing they do not. Your account decides which
              tools you get when you sign in.
            </p>
          </div>
          <div className="role-grid">
            {roles.map((role, index) => (
              <article
                key={role.name}
                className="glass-card role-card"
                data-reveal
                style={{ transitionDelay: `${index * 80}ms` }}
              >
                <div className="role-card-head">
                  <span className="feature-icon"><role.icon width={22} height={22} /></span>
                  <span className="role-where">{role.where}</span>
                </div>
                <h3>{role.name}</h3>
                <p>{role.body}</p>
                <ul>
                  {role.points.map((point) => (
                    <li key={point}><CheckIcon width={16} height={16} /> {point}</li>
                  ))}
                </ul>
              </article>
            ))}
          </div>
        </div>
      </section>

      {/* Mobile app */}
      <section id="mobile-app" className="site-section" aria-labelledby="app-title">
        <div className="site-container">
          <div className="app-band glass-card" data-reveal>
            <div className="app-band-copy">
              <p className="site-eyebrow">SolarGrid for Android</p>
              <h2 id="app-title" className="site-section-title">Your bookings, in your pocket.</h2>
              <p className="site-section-lead">
                Prosumers use the SolarGrid Android app to register, book energy and show their QR
                pass at the station. It works on Android 7.0 and newer.
              </p>
              <ul className="app-points">
                {appPoints.map((point) => (
                  <li key={point}><span><CheckIcon width={14} height={14} /></span>{point}</li>
                ))}
              </ul>
              <div className="site-hero-actions">
                <Link to="/contact" className="btn site-btn site-btn-primary site-btn-lg">
                  Request the app <ArrowRightIcon width={18} height={18} />
                </Link>
                <Link to="/#how-it-works" className="btn site-btn site-btn-ghost site-btn-lg">See how it works</Link>
              </div>
            </div>
            <PhoneMockup />
          </div>
        </div>
      </section>

      {/* Closing call to action */}
      <section className="site-section site-section-last">
        <div className="site-container">
          <div className="cta-band" data-reveal>
            <div>
              <h2>Already a member?</h2>
              <p>Sign in to manage stations, bookings and your team.</p>
            </div>
            <div className="cta-band-actions">
              <Link to="/login" className="btn site-btn site-btn-light site-btn-lg">
                Sign in <ArrowRightIcon width={18} height={18} />
              </Link>
              <Link to="/contact" className="btn site-btn site-btn-outline-light site-btn-lg">Talk to us</Link>
            </div>
          </div>
        </div>
      </section>
    </div>
  )
}
