# Rich-text link events

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode on a runtime panel (a UIDocument made
in Play mode drawing into an 800x600 target). The values are the link events received; they depend
on neither the OS nor the graphics device. The gate compares every value exactly.

`Assets/Scripts/LinksProbe.cs` sends pointer events at fractions of a label's width (at mid
height): `<link="one">AAAA</link>BBBBBBBB<link="two">CCCC</link>` is entered at its left edge (link
one), within it, off the links (the middle), at its right edge (link two) and from there back onto
link one; pressed and released over the links and off them; and left for another element. An
`<a href>` label with an address that is not a URL is entered, pressed and released, and a label
whose `<link>` is removed by a text change is used again after the change. After each step the
probe records the link events (type, link id and text, receiving element, target, phase) the label
and its parent received, in order.

- Entering a link sends PointerOverLinkTagEvent, moving on it PointerMoveLinkTagEvent, and moving
  off it onto plain text PointerOutLinkTagEvent without a link. Moving from one link straight onto
  another sends only the out event (the other link's over event comes with the next move on it).
  Down and up over a link send their events with that link; off the links nothing is sent.
- The events are sent to the label: its callbacks run in the bubble-up phase, and they bubble to its
  parent. A move sent to another element sends the label nothing.
- `<a href>` (an address that is not a URL) sends none of them on a runtime panel.
- A text change that removes the links stops them: no events after it, not even an out event.

Other releases are unmeasured.
