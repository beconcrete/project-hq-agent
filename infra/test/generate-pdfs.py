#!/usr/bin/env python3
"""Generate test contract PDFs — no external dependencies required."""

import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))


def make_pdf(lines):
    """Produce a minimal valid PDF with Helvetica text, A4 size."""
    def esc(s):
        return s.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")

    parts = ["BT", "/F1 11 Tf", "50 800 Td", "14 TL"]
    for line in lines:
        parts.append(f"({esc(line)}) Tj T*")
    parts.append("ET")
    stream = "\n".join(parts).encode("latin-1")

    obj_bodies = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842]"
            b" /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>"
        ),
        f"<< /Length {len(stream)} >>".encode() + b"\nstream\n" + stream + b"\nendstream",
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]

    out = b"%PDF-1.4\n"
    xref_offsets = []
    for i, body in enumerate(obj_bodies, 1):
        xref_offsets.append(len(out))
        out += f"{i} 0 obj\n".encode() + body + b"\nendobj\n"

    xref_start = len(out)
    out += f"xref\n0 {len(xref_offsets) + 1}\n0000000000 65535 f \n".encode()
    for off in xref_offsets:
        out += f"{off:010d} 00000 n \n".encode()
    out += (
        f"trailer\n<< /Size {len(xref_offsets) + 1} /Root 1 0 R >>\n"
        f"startxref\n{xref_start}\n%%EOF\n"
    ).encode()
    return out


CONSULTING_AGREEMENT = [
    "CONSULTING SERVICES AGREEMENT",
    "",
    "This Consulting Services Agreement (\"Agreement\") is entered into as of April 1, 2026,",
    "by and between:",
    "",
    "Client:     ABC Technologies AB, org. no. 556789-0123",
    "            Kungsgatan 12, 111 43 Stockholm, Sweden",
    "            (\"Client\")",
    "",
    "Consultant: Bjorn Eriksen Consulting AB, org. no. 556901-2345",
    "            Sveavagen 55, 113 59 Stockholm, Sweden",
    "            (\"Consultant\")",
    "",
    "1. SERVICES",
    "The Consultant agrees to provide software architecture and development consulting",
    "services as reasonably requested by the Client.",
    "",
    "2. TERM",
    "This Agreement commences on April 1, 2026, and continues for a period of six (6) months,",
    "ending on September 30, 2026, unless terminated earlier.",
    "",
    "3. COMPENSATION",
    "The Client shall pay the Consultant at a rate of SEK 1,200 per hour.",
    "Invoices are issued monthly and payable within 30 days of receipt.",
    "",
    "4. NOTICE PERIOD",
    "Either party may terminate this Agreement with thirty (30) days' written notice.",
    "",
    "5. CONFIDENTIALITY",
    "The Consultant agrees to keep all Client information confidential during and after",
    "the term of this Agreement.",
    "",
    "6. INTELLECTUAL PROPERTY",
    "All work product created by the Consultant under this Agreement is the exclusive",
    "property of the Client upon full payment.",
    "",
    "7. GOVERNING LAW",
    "This Agreement shall be governed by and construed in accordance with the laws of Sweden.",
    "Any disputes shall be resolved in Stockholm District Court.",
    "",
    "8. AUTO-RENEWAL",
    "This Agreement does not auto-renew. A new agreement must be executed to continue.",
    "",
    "Signed by authorized representatives:",
    "",
    "________________________          ________________________",
    "ABC Technologies AB               Bjorn Eriksen Consulting AB",
    "Stockholm, 2026-04-01             Stockholm, 2026-04-01",
]

NDA = [
    "NON-DISCLOSURE AGREEMENT",
    "",
    "This Non-Disclosure Agreement (\"Agreement\") is made as of March 15, 2026,",
    "by and between:",
    "",
    "Party A:    ABC Technologies AB, org. no. 556789-0123",
    "            Kungsgatan 12, 111 43 Stockholm, Sweden",
    "",
    "Party B:    XYZ Solutions AB, org. no. 556612-9876",
    "            Drottninggatan 88, 111 60 Stockholm, Sweden",
    "",
    "(each a \"Party\" and collectively the \"Parties\")",
    "",
    "1. PURPOSE",
    "The Parties intend to explore a potential business collaboration and may disclose",
    "confidential information to each other for evaluation purposes.",
    "",
    "2. DEFINITION OF CONFIDENTIAL INFORMATION",
    "\"Confidential Information\" means any non-public information disclosed by one Party",
    "to the other, including but not limited to: business plans, technical data, source code,",
    "customer lists, financial information, and trade secrets.",
    "",
    "3. OBLIGATIONS",
    "Each Party agrees to: (a) hold all Confidential Information in strict confidence;",
    "(b) not disclose Confidential Information to any third party without prior written",
    "consent; (c) use Confidential Information solely for the purpose stated herein.",
    "",
    "4. TERM",
    "This Agreement is effective as of March 15, 2026, and shall remain in force for",
    "a period of two (2) years, expiring on March 14, 2028.",
    "",
    "5. NOTICE PERIOD",
    "Either party may terminate with sixty (60) days written notice. Confidentiality",
    "obligations survive termination for the full two-year term.",
    "",
    "6. EXCEPTIONS",
    "Obligations do not apply to information that: (a) is or becomes publicly known",
    "through no breach of this Agreement; (b) was already known to the receiving Party;",
    "(c) is required to be disclosed by law or court order.",
    "",
    "7. NO LICENSE",
    "Nothing herein grants any license or right to use the other Party's intellectual",
    "property except as explicitly stated.",
    "",
    "8. AUTO-RENEWAL",
    "This Agreement automatically renews for successive one-year periods unless either",
    "Party provides written notice of non-renewal at least sixty (60) days before the",
    "end of the then-current term.",
    "",
    "9. GOVERNING LAW",
    "This Agreement is governed by Swedish law. Disputes shall be resolved exclusively",
    "in Stockholm District Court.",
    "",
    "Signed by authorized representatives:",
    "",
    "________________________          ________________________",
    "ABC Technologies AB               XYZ Solutions AB",
    "Stockholm, 2026-03-15             Stockholm, 2026-03-15",
]


def main():
    docs = {
        "consulting-assignment.pdf": CONSULTING_AGREEMENT,
        "nda.pdf": NDA,
    }
    for filename, lines in docs.items():
        path = os.path.join(SCRIPT_DIR, filename)
        pdf_bytes = make_pdf(lines)
        with open(path, "wb") as f:
            f.write(pdf_bytes)
        print(f"  Generated {filename} ({len(pdf_bytes)} bytes)")


if __name__ == "__main__":
    main()
