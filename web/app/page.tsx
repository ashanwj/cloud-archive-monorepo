'use client'

import { useState } from 'react'
import DropZone from './components/DropZone'
import ResultCard from './components/ResultCard'
import type { DocumentResponse } from './types'
import styles from './page.module.css'

type UploadState =
  | { status: 'idle' }
  | { status: 'uploading' }
  | { status: 'success'; data: DocumentResponse }
  | { status: 'error'; message: string }

export default function Home() {
  const [state, setState] = useState<UploadState>({ status: 'idle' })

  async function handleFile(file: File) {
    setState({ status: 'uploading' })

    const body = new FormData()
    body.append('file', file)

    try {
      const res = await fetch('/api/documents/upload', { method: 'POST', body })

      if (!res.ok) {
        const json = await res.json().catch(() => ({}))
        const message: string = json?.detail ?? json?.title ?? `Upload failed (HTTP ${res.status})`
        setState({ status: 'error', message })
        return
      }

      const data: DocumentResponse = await res.json()
      setState({ status: 'success', data })
    } catch {
      setState({ status: 'error', message: 'Network error — is the .NET API running?' })
    }
  }

  function handleError(message: string) {
    setState({ status: 'error', message })
  }

  function reset() {
    setState({ status: 'idle' })
  }

  return (
    <main>
      <h1>CloudArchive</h1>
      <p className="subtitle">Upload a document for AI-powered archiving and summarisation</p>

      <DropZone
        onFile={handleFile}
        onError={handleError}
        isUploading={state.status === 'uploading'}
      />

      {state.status === 'uploading' && (
        <p className={styles.uploading}>Uploading to S3 and generating summary…</p>
      )}

      {state.status === 'error' && (
        <div className={styles.errorBanner} role="alert">
          <span><strong>Error:</strong> {state.message}</span>
          <button className={styles.resetBtn} onClick={reset}>Try again</button>
        </div>
      )}

      {state.status === 'success' && (
        <>
          <ResultCard result={state.data} />
          <button className={styles.uploadAnother} onClick={reset}>
            Upload another document
          </button>
        </>
      )}
    </main>
  )
}
