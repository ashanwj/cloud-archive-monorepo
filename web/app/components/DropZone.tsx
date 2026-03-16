'use client'

import { useCallback, useRef, useState } from 'react'
import styles from './DropZone.module.css'

const ACCEPTED_EXTENSIONS = ['.txt', '.md', '.csv', '.json'] as const
const MAX_SIZE_BYTES = 10 * 1024 * 1024 // 10 MB

interface Props {
  onFile: (file: File) => void
  onError: (message: string) => void
  isUploading: boolean
}

function validateFile(file: File): string | null {
  const ext = '.' + (file.name.split('.').pop()?.toLowerCase() ?? '')
  if (!ACCEPTED_EXTENSIONS.includes(ext as typeof ACCEPTED_EXTENSIONS[number])) {
    return `Unsupported type "${ext}". Accepted: ${ACCEPTED_EXTENSIONS.join(', ')}`
  }
  if (file.size > MAX_SIZE_BYTES) {
    return 'File exceeds the 10 MB limit.'
  }
  return null
}

export default function DropZone({ onFile, onError, isUploading }: Props) {
  const [isDragging, setIsDragging] = useState(false)
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const dragCounter = useRef(0)
  const inputRef = useRef<HTMLInputElement>(null)

  const handleFile = useCallback(
    (file: File) => {
      const error = validateFile(file)
      if (error) { onError(error); return }
      setSelectedFile(file)
      onFile(file)
    },
    [onFile, onError],
  )

  const onDragEnter = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    dragCounter.current++
    setIsDragging(true)
  }, [])

  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'copy'
  }, [])

  const onDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    dragCounter.current--
    if (dragCounter.current === 0) setIsDragging(false)
  }, [])

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault()
      dragCounter.current = 0
      setIsDragging(false)
      const file = e.dataTransfer.files[0]
      if (file) handleFile(file)
    },
    [handleFile],
  )

  const onInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0]
      if (file) handleFile(file)
      e.target.value = ''
    },
    [handleFile],
  )

  const onKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      inputRef.current?.click()
    }
  }, [])

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label="File upload drop zone — click or drag a file here"
      aria-disabled={isUploading}
      className={`${styles.zone} ${isDragging ? styles.dragging : ''}`}
      onDragEnter={onDragEnter}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      onClick={() => !isUploading && inputRef.current?.click()}
      onKeyDown={onKeyDown}
    >
      <input
        ref={inputRef}
        type="file"
        accept={ACCEPTED_EXTENSIONS.join(',')}
        className={styles.hiddenInput}
        onChange={onInputChange}
        tabIndex={-1}
      />

      <div className={styles.icon}>
        {isUploading ? '⏳' : isDragging ? '📂' : '📄'}
      </div>

      {selectedFile && !isUploading ? (
        <>
          <p className={styles.filename}>{selectedFile.name}</p>
          <p className={styles.secondary}>Click to change file</p>
        </>
      ) : (
        <>
          <p className={styles.primary}>
            {isUploading ? 'Uploading…' : 'Drop a document here'}
          </p>
          <p className={styles.secondary}>or click to browse</p>
          <p className={styles.types}>
            {ACCEPTED_EXTENSIONS.join('  ·  ')} &nbsp;·&nbsp; max 10 MB
          </p>
        </>
      )}
    </div>
  )
}
