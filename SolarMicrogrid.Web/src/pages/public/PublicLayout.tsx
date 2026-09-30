import { useEffect, useState, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { getHomePathForRole } from '../../auth/roleRouting'
import { useAuth } from '../../auth/useAuth'
import { ThemeToggle } from '../../components/ThemeToggle'
import {
  ArrowRightIcon,
  CloseIcon,
  MailIcon,
  MapPinIcon,
  MenuIcon,
  PhoneIcon,
  SolarGridLogoIcon,
} from '../../components/icons'
import { contactDetails, publicNavigation } from './siteContent'

/** Shell for the public site: glass header, page content and footer. */
export function PublicLayout({ children }: { children?: ReactNode }) {
  const { session } = useAuth()
  const location = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)
  const [scrolled, setScrolled] = useState(false)

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 12)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  // Honour in-page anchors such as /#features, otherwise start each page at the top.
  useEffect(() => {
    if (location.hash) {
      const target = document.getElementById(location.hash.slice(1))
      if (target) {
        target.scrollIntoView({ behavior: 'smooth', block: 'start' })
        return
      }
    }
    window.scrollTo({ top: 0 })
  }, [location.pathname, location.hash])

  useEffect(() => {
    if (!menuOpen) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMenuOpen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [menuOpen])

  const closeMenu = () => setMenuOpen(false)
  const workspacePath = session ? getHomePathForRole(session.role) : null

  const accountAction = workspacePath ? (
    <Link to={workspacePath} className="btn site-btn site-btn-primary" onClick={closeMenu}>
      Open workspace <ArrowRightIcon width={17} height={17} />
    </Link>
  ) : (
    <Link to="/login" className="btn site-btn site-btn-primary" onClick={closeMenu}>
      Sign in <ArrowRightIcon width={17} height={17} />
    </Link>
  )

  return (
    <div className="site-shell">
      <a className="skip-link" href="#site-main">Skip to content</a>
      <div className="site-backdrop" aria-hidden="true" />

      <header className={`site-header ${scrolled ? 'is-scrolled' : ''}`}>
        <div className="site-header-inner">
          <Link to="/" className="site-brand" aria-label="SolarGrid Exchange home">
            <span className="brand-mark" aria-hidden="true"><SolarGridLogoIcon /></span>
            <span className="site-brand-name">SolarGrid <span>Exchange</span></span>
          </Link>

          <nav className="site-nav d-none d-lg-flex" aria-label="Main">
            {publicNavigation.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end
                className={({ isActive }) => `site-nav-link ${isActive && !item.to.includes('#') && !location.hash ? 'active' : ''}`}
              >
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="site-header-actions">
            <ThemeToggle />
            <span className="d-none d-sm-inline-flex">{accountAction}</span>
            <button
              type="button"
              className="sg-icon-button d-lg-none"
              aria-expanded={menuOpen}
              aria-controls="site-mobile-menu"
              aria-label={menuOpen ? 'Close menu' : 'Open menu'}
              onClick={() => setMenuOpen((open) => !open)}
            >
              {menuOpen ? <CloseIcon /> : <MenuIcon />}
            </button>
          </div>
        </div>

        <div id="site-mobile-menu" className={`site-mobile-menu d-lg-none ${menuOpen ? 'is-open' : ''}`} hidden={!menuOpen}>
          <nav aria-label="Main mobile">
            {publicNavigation.map((item) => (
              <Link key={item.to} to={item.to} className="site-mobile-link" onClick={closeMenu}>
                {item.label}
                <ArrowRightIcon width={16} height={16} />
              </Link>
            ))}
          </nav>
          <div className="mt-3">{accountAction}</div>
        </div>
      </header>

      <main id="site-main" className="site-main" tabIndex={-1}>
        {children ?? <Outlet />}
      </main>

      <footer className="site-footer">
        <div className="site-container">
          <div className="site-footer-grid">
            <div className="site-footer-brand">
              <Link to="/" className="site-brand">
                <span className="brand-mark" aria-hidden="true"><SolarGridLogoIcon /></span>
                <span className="site-brand-name">SolarGrid <span>Exchange</span></span>
              </Link>
              <p>
                Local energy. Shared possibility. We help Sri Lankan communities book clean solar
                power from stations close to home.
              </p>
            </div>

            <div>
              <h2 className="site-footer-title">Explore</h2>
              <ul className="site-footer-links">
                <li><Link to="/#features">Features</Link></li>
                <li><Link to="/#how-it-works">How it works</Link></li>
                <li><Link to="/#mobile-app">Mobile app</Link></li>
                <li><Link to="/about">About us</Link></li>
              </ul>
            </div>

            <div>
              <h2 className="site-footer-title">Account</h2>
              <ul className="site-footer-links">
                <li><Link to="/login">Sign in</Link></li>
                <li><Link to="/contact">Get the app</Link></li>
                <li><Link to="/contact#faq">Questions</Link></li>
                <li><Link to="/contact">Support</Link></li>
              </ul>
            </div>

            <div>
              <h2 className="site-footer-title">Contact</h2>
              <ul className="site-footer-contact">
                <li>
                  <PhoneIcon width={17} height={17} />
                  <a href={contactDetails.phoneHref}>{contactDetails.phoneDisplay}</a>
                </li>
                <li>
                  <MailIcon width={17} height={17} />
                  <a href={`mailto:${contactDetails.email}`}>{contactDetails.email}</a>
                </li>
                <li>
                  <MapPinIcon width={17} height={17} />
                  <span>{contactDetails.addressLines.join(', ')}</span>
                </li>
              </ul>
            </div>
          </div>

          <div className="site-footer-bottom">
            <span>&copy; {new Date().getFullYear()} SolarGrid Exchange. All rights reserved.</span>
            <span>Made in Sri Lanka</span>
          </div>
        </div>
      </footer>
    </div>
  )
}
